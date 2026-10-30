using System.Collections.Generic;
using UnityEngine;

namespace BulletHeaven.Unity
{
    /// <summary>How a character or bug model is fitted: scaled so its length (or height) is <see cref="Size"/>,
    /// centred, stood on the ground and turned by <see cref="YawDegrees"/> (Godot-style) to face +X.</summary>
    public readonly struct ModelSpec
    {
        public readonly string Name;
        public readonly float Size;
        public readonly float YawDegrees;
        public readonly bool FitHeight;

        public ModelSpec(string name, float size, float yawDegrees, bool fitHeight = false)
        {
            Name = name;
            Size = size;
            YawDegrees = yawDegrees;
            FitHeight = fitHeight;
        }
    }

    /// <summary>A model's mesh, materials per submesh and the matrix that fits it, for instanced drawing.</summary>
    public sealed class FittedMesh
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Matrix4x4 Fit;
    }

    /// <summary>
    /// Loads the generated models (Resources/Models/*.glb, imported by glTFast) and textures. Same rules
    /// as the Godot version: props are modelled at game size with their origin at the bottom centre;
    /// characters and bugs are fitted by a <see cref="ModelSpec"/>. Everything returns null when the
    /// file isn't there so callers can fall back to primitives.
    ///
    /// glTFast imports glTF mirrored on X (its way to left-handed), while the game mirrors the ground
    /// plane on Z (see <see cref="Conv"/>); a half turn about Y turns one into the other.
    /// </summary>
    public static class ModelLibrary
    {
        public static readonly ModelSpec Runner = new ModelSpec("runner", 1.35f, 90f, fitHeight: true);
        public static readonly ModelSpec Ant = new ModelSpec("ant", 1.4f, 90f);
        public static readonly ModelSpec Beetle = new ModelSpec("beetle", 1.3f, 90f);
        public static readonly ModelSpec Gem = new ModelSpec("gem", 1f, 0f);

        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        public static GameObject Prefab(string name)
        {
            if (!Prefabs.TryGetValue(name, out var prefab))
                Prefabs[name] = prefab = Resources.Load<GameObject>("Models/" + name);
            return prefab;
        }

        public static Texture2D LoadTexture(string name) => Resources.Load<Texture2D>("Textures/" + name);

        /// <summary>
        /// A prop, origin at its bottom centre, stretched to <paramref name="exactSize"/> (width, height, depth)
        /// if given. Its materials are its own copies (for tinting and fading) and are added to
        /// <paramref name="materials"/>. <paramref name="inModelSpace"/>: being attached inside another model
        /// (a weapon in a hand), which is already mirrored the same way, so no half turn.
        /// </summary>
        public static GameObject LoadProp(string name, Vector3? exactSize = null, Color? tint = null, List<Material> materials = null,
            bool inModelSpace = false)
        {
            var prefab = Prefab(name);
            if (prefab == null)
                return null;

            var root = new GameObject(name);
            var turn = new GameObject("turn");
            turn.transform.SetParent(root.transform, false);
            if (!inModelSpace)
                turn.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var model = Object.Instantiate(prefab, turn.transform, false);
            if (exactSize is Vector3 size && Measure(model.transform) is Bounds box)
            {
                var scale = new Vector3(size.x / box.size.x, size.y / box.size.y, size.z / box.size.z);
                model.transform.localScale = Vector3.Scale(model.transform.localScale, scale);
                model.transform.localPosition = -Vector3.Scale(new Vector3(box.center.x, box.min.y, box.center.z), scale);
            }
            OwnMaterials(model, tint, materials);
            return root;
        }

        /// <summary>A character model (the runner) fitted by <paramref name="spec"/>, facing +X; child nodes keep their names.</summary>
        public static GameObject LoadScene(ModelSpec spec, List<Material> materials = null)
        {
            var prefab = Prefab(spec.Name);
            if (prefab == null)
                return null;

            var root = new GameObject(spec.Name);
            var model = Object.Instantiate(prefab, root.transform, false);
            if (Measure(model.transform) is Bounds box)
            {
                var fit = Fit(spec, box);
                model.transform.localPosition = fit.GetColumn(3);
                model.transform.localRotation = fit.rotation;
                model.transform.localScale = fit.lossyScale;
            }
            OwnMaterials(model, null, materials);
            return root;
        }

        /// <summary>The first mesh in the model with converted materials and the matrix that fits it.</summary>
        public static FittedMesh LoadMesh(ModelSpec spec)
        {
            var prefab = Prefab(spec.Name);
            if (prefab == null)
                return null;
            var filter = prefab.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogWarning($"Models/{spec.Name} contains no mesh; using the placeholder.");
                return null;
            }

            var toRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var bounds = Transform(filter.sharedMesh.bounds, toRoot);
            var renderer = filter.GetComponent<MeshRenderer>();
            var sources = renderer != null ? renderer.sharedMaterials : new Material[0];
            var materials = new Material[filter.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = new Material(Looks.FromGltf(i < sources.Length ? sources[i] : null));
            return new FittedMesh { Mesh = filter.sharedMesh, Materials = materials, Fit = Fit(spec, bounds) * toRoot };
        }

        public static Transform FindNode(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform child in root)
            {
                var hit = FindNode(child, name);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        /// <summary>Scale to the spec's size, centre, stand on the ground, then turn (with the mirror half turn).</summary>
        private static Matrix4x4 Fit(ModelSpec spec, Bounds box)
        {
            float measured = spec.FitHeight ? box.size.y : Mathf.Max(box.size.x, box.size.z);
            float scale = measured > 1e-4f ? spec.Size / measured : 1f;
            var turn = Quaternion.Euler(0f, -(spec.YawDegrees + 180f), 0f);
            var offset = new Vector3(-box.center.x, -box.min.y, -box.center.z);
            return Matrix4x4.TRS(Vector3.zero, turn, Vector3.one * scale) * Matrix4x4.Translate(offset);
        }

        /// <summary>Bounds of every mesh under <paramref name="root"/>, in root's local space.</summary>
        private static Bounds? Measure(Transform root)
        {
            Bounds? total = null;
            var toRoot = root.worldToLocalMatrix;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                    continue;
                var box = Transform(filter.sharedMesh.bounds, toRoot * filter.transform.localToWorldMatrix);
                if (total is Bounds t)
                {
                    t.Encapsulate(box);
                    total = t;
                }
                else
                {
                    total = box;
                }
            }
            return total;
        }

        private static Bounds Transform(Bounds box, Matrix4x4 matrix)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(box.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(i % 2 == 0 ? box.min.x : box.max.x, (i / 2) % 2 == 0 ? box.min.y : box.max.y, i / 4 == 0 ? box.min.z : box.max.z);
                result.Encapsulate(matrix.MultiplyPoint3x4(corner));
            }
            return result;
        }

        private static void OwnMaterials(GameObject model, Color? tint, List<Material> materials)
        {
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var shared = renderer.sharedMaterials;
                var own = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    own[i] = new Material(Looks.FromGltf(shared[i]));
                    if (tint is Color color)
                        own[i].SetColor("_BaseColor", color); // multiplies the texture: paint over bare wood
                    materials?.Add(own[i]);
                }
                renderer.sharedMaterials = own;
            }
        }
    }

    /// <summary>Primitive meshes for placeholders and fallbacks, built once.</summary>
    public static class Meshes
    {
        private static Mesh _cube, _sphere, _plane, _wedge;
        private static readonly Dictionary<float, Mesh> Cylinders = new Dictionary<float, Mesh>();

        public static Mesh Cube => _cube != null ? _cube : _cube = Builtin(PrimitiveType.Cube);
        public static Mesh Sphere => _sphere != null ? _sphere : _sphere = Builtin(PrimitiveType.Sphere);

        /// <summary>1 x 1 quad lying flat facing up, UVs 0..1.</summary>
        public static Mesh Plane => _plane != null ? _plane : _plane = BuildPlane();

        /// <summary>1 x 1 x 1 right-angle wedge rising towards +X, standing on y = -0.5 (centred like a cube).</summary>
        public static Mesh Wedge => _wedge != null ? _wedge : _wedge = BuildWedge();

        /// <summary>Cylinder of diameter 1 and height 1 (centred), its top radius scaled by <paramref name="topScale"/>.</summary>
        public static Mesh Cylinder(float topScale = 1f)
        {
            if (!Cylinders.TryGetValue(topScale, out var mesh))
                Cylinders[topScale] = mesh = BuildCylinder(topScale, 32);
            return mesh;
        }

        private static Mesh Builtin(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(temp);
            return mesh;
        }

        private static Mesh BuildPlane()
        {
            var mesh = new Mesh { name = "Plane" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildWedge()
        {
            // Flat-shaded: each face has its own vertices.
            Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f), b = new Vector3(0.5f, -0.5f, -0.5f), c = new Vector3(0.5f, -0.5f, 0.5f), d = new Vector3(-0.5f, -0.5f, 0.5f);
            Vector3 e = new Vector3(0.5f, 0.5f, -0.5f), f = new Vector3(0.5f, 0.5f, 0.5f);
            var faces = new List<Vector3[]>
            {
                new[] { a, b, c, d },   // bottom
                new[] { b, e, f, c },   // tall end (+X)
                new[] { a, d, f, e },   // slope
                new[] { a, e, b },      // side -Z
                new[] { d, c, f },      // side +Z
            };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var face in faces)
            {
                int start = vertices.Count;
                vertices.AddRange(face);
                for (int i = 1; i < face.Length - 1; i++)
                {
                    triangles.Add(start);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                }
            }
            var mesh = new Mesh { name = "Wedge", vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            FixWinding(mesh);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flips any triangle whose normal points into the shape (towards the centre).</summary>
        private static void FixWinding(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var normal = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                var center = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                // For Unity's front faces (clockwise seen from outside) this cross product points outwards.
                if (Vector3.Dot(normal, center - new Vector3(0.1f, -0.1f, 0f)) < 0f)
                    (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }
            mesh.triangles = t;
        }

        private static Mesh BuildCylinder(float topScale, int sides)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            float bottom = 0.5f, top = 0.5f * topScale;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var normal = (dir + Vector3.up * (bottom - top)).normalized;
                vertices.Add(dir * bottom + Vector3.down * 0.5f);
                vertices.Add(dir * top + Vector3.up * 0.5f);
                normals.Add(normal);
                normals.Add(normal);
                if (i < sides)
                {
                    int k = i * 2;
                    triangles.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
                }
            }
            if (topScale == 1f)
            {
                // Caps (open-topped frustums are lamp shades).
                foreach (float y in new[] { -0.5f, 0.5f })
                {
                    int center = vertices.Count;
                    vertices.Add(new Vector3(0f, y, 0f));
                    normals.Add(new Vector3(0f, Mathf.Sign(y), 0f));
                    for (int i = 0; i <= sides; i++)
                    {
                        float angle = i * Mathf.PI * 2f / sides;
                        vertices.Add(new Vector3(Mathf.Cos(angle) * 0.5f, y, Mathf.Sin(angle) * 0.5f));
                        normals.Add(new Vector3(0f, Mathf.Sign(y), 0f));
                        if (i < sides)
                        {
                            if (y > 0f)
                                triangles.AddRange(new[] { center, center + i + 2, center + i + 1 });
                            else
                                triangles.AddRange(new[] { center, center + i + 1, center + i + 2 });
                        }
                    }
                }
            }
            var mesh = new Mesh { name = "Cylinder", vertices = vertices.ToArray(), normals = normals.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
