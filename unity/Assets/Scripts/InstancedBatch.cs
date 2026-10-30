using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// Draws any number of copies of one mesh with GPU instancing, in chunks of 1023 (Unity's limit
    /// per call). Each frame: Begin, Add per instance, Draw. Optional per-instance vectors go to
    /// instanced shader properties (e.g. the bug walk cycle's _Anim).
    /// </summary>
    public sealed class InstancedBatch
    {
        private const int Chunk = 1023;
        private static readonly Bounds Everywhere = new Bounds(Vector3.zero, Vector3.one * 2000f);

        private readonly Mesh _mesh;
        private readonly Material[] _materials;
        private readonly Matrix4x4 _fit;
        private readonly ShadowCastingMode _shadows;
        private readonly string _data0Name;
        private readonly string _data1Name;
        private readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
        private readonly List<Vector4> _data0 = new List<Vector4>();
        private readonly List<Vector4> _data1 = new List<Vector4>();
        private readonly Matrix4x4[] _chunkMatrices = new Matrix4x4[Chunk];
        private readonly List<(Vector4[] Data0, Vector4[] Data1, MaterialPropertyBlock Block)> _chunks =
            new List<(Vector4[], Vector4[], MaterialPropertyBlock)>();

        public int Count => _matrices.Count;

        public InstancedBatch(Mesh mesh, Material[] materials, Matrix4x4 fit, bool castShadows = true, string data0 = null, string data1 = null)
        {
            _mesh = mesh;
            _materials = materials;
            _fit = fit;
            _shadows = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            _data0Name = data0;
            _data1Name = data1;
            foreach (var material in materials)
                material.enableInstancing = true;
        }

        public InstancedBatch(Mesh mesh, Material material, bool castShadows = true, string data0 = null, string data1 = null)
            : this(mesh, new[] { material }, Matrix4x4.identity, castShadows, data0, data1) { }

        public void Begin()
        {
            _matrices.Clear();
            _data0.Clear();
            _data1.Clear();
        }

        public void Add(Vector3 position, Quaternion rotation, Vector3 scale, Vector4 data0 = default, Vector4 data1 = default)
        {
            _matrices.Add(Matrix4x4.TRS(position, rotation, scale) * _fit);
            _data0.Add(data0);
            _data1.Add(data1);
        }

        public void Draw()
        {
            int chunkIndex = 0;
            for (int start = 0; start < _matrices.Count; start += Chunk, chunkIndex++)
            {
                int count = Mathf.Min(Chunk, _matrices.Count - start);
                _matrices.CopyTo(start, _chunkMatrices, 0, count);

                // A block per chunk: the draw happens later, so each needs its own data.
                if (chunkIndex == _chunks.Count)
                    _chunks.Add((new Vector4[Chunk], new Vector4[Chunk], new MaterialPropertyBlock()));
                var chunk = _chunks[chunkIndex];
                if (_data0Name != null)
                {
                    _data0.CopyTo(start, chunk.Data0, 0, count);
                    chunk.Block.SetVectorArray(_data0Name, chunk.Data0);
                }
                if (_data1Name != null)
                {
                    _data1.CopyTo(start, chunk.Data1, 0, count);
                    chunk.Block.SetVectorArray(_data1Name, chunk.Data1);
                }

                for (int submesh = 0; submesh < _mesh.subMeshCount && submesh < _materials.Length; submesh++)
                {
                    var parameters = new RenderParams(_materials[submesh])
                    {
                        shadowCastingMode = _shadows,
                        receiveShadows = true,
                        matProps = chunk.Block,
                        worldBounds = Everywhere,
                    };
                    Graphics.RenderMeshInstanced(parameters, _mesh, submesh, _chunkMatrices, count);
                }
            }
        }
    }
}
