using System.Collections.Generic;
using BulletHeaven.Ecs;
using UnityEngine;
using Core = BulletHeaven.Numerics;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// Quads rebuilt every frame into one mesh and drawn with one material: the cheapest way to draw
    /// thousands of camera-facing glows (sparks, bullet streaks) and ribbons (beams, ring fronts).
    /// </summary>
    public sealed class QuadBatch
    {
        private static readonly Bounds Everywhere = new Bounds(Vector3.zero, Vector3.one * 4000f);
        private readonly Mesh _mesh = new Mesh { name = "QuadBatch", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        private readonly Material _material;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _indices = new List<int>();

        public QuadBatch(Material material)
        {
            _material = material;
            _mesh.MarkDynamic();
        }

        public void Begin()
        {
            _vertices.Clear();
            _colors.Clear();
            _uvs.Clear();
            _indices.Clear();
        }

        /// <summary>A quad from four corners (a-b along U, a-d along V).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int i = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);
            _uvs.Add(new Vector2(0f, 0f));
            _uvs.Add(new Vector2(1f, 0f));
            _uvs.Add(new Vector2(1f, 1f));
            _uvs.Add(new Vector2(0f, 1f));
            for (int k = 0; k < 4; k++)
                _colors.Add(color);
            _indices.Add(i);
            _indices.Add(i + 1);
            _indices.Add(i + 2);
            _indices.Add(i);
            _indices.Add(i + 2);
            _indices.Add(i + 3);
        }

        /// <summary>A camera-facing quad centred on <paramref name="at"/>, stretched along <paramref name="along"/> (world units).</summary>
        public void Billboard(Vector3 at, Vector3 right, Vector3 up, float width, float height, Color color)
        {
            var r = right * (width * 0.5f);
            var u = up * (height * 0.5f);
            Quad(at - r - u, at + r - u, at + r + u, at - r + u, color);
        }

        /// <summary>A ribbon from <paramref name="from"/> to <paramref name="to"/> that faces the camera (V runs across it).</summary>
        public void Ribbon(Vector3 from, Vector3 to, float width, Color color, Vector3 camera)
        {
            var along = to - from;
            var side = Vector3.Cross(along, (from + to) * 0.5f - camera).normalized * (width * 0.5f);
            Quad(from - side, to - side, to + side, from + side, color);
        }

        public void Draw()
        {
            _mesh.Clear();
            if (_vertices.Count == 0)
                return;
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetIndices(_indices, MeshTopology.Triangles, 0, false);
            _mesh.bounds = Everywhere;
            Graphics.RenderMesh(new RenderParams(_material) { worldBounds = Everywhere }, _mesh, 0, Matrix4x4.identity);
        }
    }

    /// <summary>
    /// The arcade layer, after DoDonPachi: glowing bullet streaks, laser beams with a white-hot core and
    /// travelling energy rings, burning ring fronts for the TD Mortar, dark void orbs with a violet halo
    /// and sucked-in motes, and sparks on every hit, kill, burst and pickup (from <see cref="GameState.Events"/>).
    /// </summary>
    public sealed class Effects
    {
        private const int MaxParticles = 6000;

        private struct Particle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Color Color;
            public float Life;
            public float MaxLife;
            public float Size;
            public float Stretch; // streak length per unit of speed
            public float Drag;
            public float Gravity;
        }

        private readonly Particle[] _particles = new Particle[MaxParticles];
        private int _count;
        private readonly QuadBatch _dots;  // round glows: sparks, streaks, flashes, orb halos
        private readonly QuadBatch _bands; // ribbons: beams and ring fronts
        private Vector3 _right, _up, _forward, _camera;
        private float _time;

        public Effects()
        {
            _dots = new QuadBatch(Glow(0f, 2.2f));
            _bands = new QuadBatch(Glow(1f, 2.6f));
        }

        private static Material Glow(float shape, float intensity)
        {
            var material = new Material(Shader.Find("BulletHeaven/Glow"));
            material.SetFloat("_Shape", shape);
            material.SetFloat("_Intensity", intensity);
            return material;
        }

        public void Begin(Camera camera, float time)
        {
            _right = camera.transform.right;
            _up = camera.transform.up;
            _forward = camera.transform.forward;
            _camera = camera.transform.position;
            _time = time;
            _dots.Begin();
            _bands.Begin();
        }

        public void End(float dt)
        {
            UpdateParticles(dt);
            _dots.Draw();
            _bands.Draw();
        }

        // --- things to draw this frame -------------------------------------------------------

        /// <summary>A bullet: a white-hot core inside a coloured streak along its flight.</summary>
        public void Bullet(Vector3 at, Vector3 velocity, Color color)
        {
            Streak(at, velocity, 1.1f, 0.42f, color * new Color(1f, 1f, 1f, 0.9f));
            Streak(at, velocity, 0.65f, 0.16f, new Color(1f, 1f, 0.9f, 1f));
        }

        public void Dot(Vector3 at, float size, Color color) => _dots.Billboard(at, _right, _up, size, size, color);

        /// <summary>A laser beam: wide soft halo, bright body, white core, energy rings running along it and flares at both ends.</summary>
        public void Laser(Vector3 from, Vector3 to, float width)
        {
            float flicker = 1f + 0.12f * Mathf.Sin(_time * 70f) + 0.06f * Mathf.Sin(_time * 131f);
            float w = width * flicker;
            _bands.Ribbon(from, to, w * 3.4f, new Color(0.25f, 0.5f, 1f, 0.55f), _camera);
            _bands.Ribbon(from, to, w * 1.7f, new Color(0.45f, 0.85f, 1f, 0.9f), _camera);
            _bands.Ribbon(from, to, w * 0.65f, new Color(1f, 1f, 1f, 1f), _camera);

            var along = to - from;
            float length = along.magnitude;
            var dir = along / Mathf.Max(length, 1e-3f);
            for (float d = (_time * 28f) % 1.4f; d < length; d += 1.4f)
                Dot(from + dir * d, w * 2.6f, new Color(0.5f, 0.8f, 1f, 0.45f));
            Dot(from, w * 5f * flicker, new Color(0.7f, 0.9f, 1f, 1f));
            Dot(to, w * 4f, new Color(0.5f, 0.8f, 1f, 0.7f));
            if (Random.value < 0.6f)
                Spark(from + dir * Random.Range(0f, length), Random.onUnitSphere * 6f + dir * 8f, new Color(0.6f, 0.85f, 1f), 0.25f, 0.18f);
        }

        /// <summary>A TD Mortar ring: its burning front (the exact gameplay shape) and a hot inner edge.</summary>
        public void Ring(Core.Vector2 center, in PulseRing ring)
        {
            const int segments = 96;
            float fade = Mathf.Clamp01(1f - ring.Radius / ring.MaxRadius) * Mathf.Clamp01(ring.Age * 8f);
            var outer = new Color(1f, 0.42f, 0.08f, 0.85f * fade);
            var inner = new Color(1f, 0.9f, 0.55f, 0.9f * fade);
            float y = ring.Height + 0.12f;
            Vector3 Point(float angle, float radius) =>
                Conv.World(new Core.Vector2(center.X + Mathf.Cos(angle) * radius, center.Y + Mathf.Sin(angle) * radius), y);

            float previousFront = ring.FrontAt(0f);
            for (int i = 1; i <= segments; i++)
            {
                float a0 = (i - 1) * Mathf.PI * 2f / segments, a1 = i * Mathf.PI * 2f / segments;
                float front = ring.FrontAt(a1);
                float half = ring.Width * 0.5f;
                _bands.Quad(Point(a0, previousFront - half), Point(a1, front - half), Point(a1, front + half), Point(a0, previousFront + half), outer);
                float core = ring.Width * 0.14f;
                _bands.Quad(Point(a0, previousFront - core), Point(a1, front - core), Point(a1, front + core), Point(a0, previousFront + core), inner);
                previousFront = front;
            }
            // Embers lifting off the front.
            if (Random.value < 0.5f * fade)
            {
                float a = Random.value * Mathf.PI * 2f;
                Spark(Point(a, ring.FrontAt(a)), new Vector3(0f, 2.5f, 0f) + Random.insideUnitSphere, new Color(1f, 0.55f, 0.15f), 0.5f, 0.22f, gravity: -1f);
            }
        }

        /// <summary>The violet halo round a void orb, with motes being sucked in.</summary>
        public void VoidOrb(Vector3 at, float pullRadius, float core)
        {
            float pulse = 1f + 0.15f * Mathf.Sin(_time * 9f + at.x);
            Dot(at, core * 3.2f * pulse, new Color(0.55f, 0.2f, 1f, 0.8f));
            Dot(at, core * 1.4f, new Color(0.9f, 0.7f, 1f, 0.6f));
            if (Random.value < 0.8f)
            {
                var offset = Random.onUnitSphere * pullRadius;
                offset.y *= 0.3f;
                Spark(at + offset, -offset * 1.6f, new Color(0.7f, 0.4f, 1f), 0.55f, 0.16f, drag: 0f);
            }
        }

        // --- events ------------------------------------------------------------------------------

        /// <summary>Sparks for everything that happened in the simulation this frame.</summary>
        public void Play(List<GameEvent> events, Vector3 gun)
        {
            foreach (var e in events)
            {
                var at = Conv.World(e.Position, e.Height);
                var dir = new Vector3(e.Direction.X, 0f, -e.Direction.Y);
                switch (e.Kind)
                {
                    case GameEventKind.Hit:
                        var color = WeaponColor((WeaponKind)e.Tag);
                        Dot(at, 0.7f, color * new Color(1f, 1f, 1f, 0.9f));
                        for (int i = 0; i < 3; i++)
                            Spark(at, (Random.onUnitSphere + dir * 1.5f) * Random.Range(5f, 11f), color, Random.Range(0.12f, 0.28f), 0.12f);
                        break;
                    case GameEventKind.Kill:
                        Flash(at, e.Tag == (int)EnemyKind.Queen ? 7f : 1.8f, new Color(1f, 0.7f, 0.3f));
                        int sparks = e.Tag == (int)EnemyKind.Queen ? 80 : 10;
                        for (int i = 0; i < sparks; i++)
                            Spark(at, Random.onUnitSphere * Random.Range(4f, 12f) + Vector3.up * 3f, new Color(1f, Random.Range(0.45f, 0.8f), 0.15f),
                                Random.Range(0.2f, 0.45f), 0.14f, gravity: 14f);
                        for (int i = 0; i < 5; i++)
                            Spark(at, Random.onUnitSphere * 5f + Vector3.up * 5f, new Color(0.5f, 0.12f, 0.06f), 0.6f, 0.2f, gravity: 22f, stretch: 0f);
                        break;
                    case GameEventKind.Burst:
                        var burst = WeaponColor((WeaponKind)e.Tag);
                        Flash(at, 5f, burst);
                        for (int i = 0; i < 36; i++)
                            Spark(at, Random.onUnitSphere * Random.Range(6f, 16f), burst, Random.Range(0.25f, 0.55f), 0.18f, gravity: 4f);
                        break;
                    case GameEventKind.Muzzle:
                        if (e.Tag != (int)WeaponKind.TdMortar)
                            Flash(gun, e.Tag == (int)WeaponKind.Minigun ? 0.7f : 1.1f, WeaponColor((WeaponKind)e.Tag), 0.06f);
                        break;
                    case GameEventKind.Pulse:
                        Flash(at + Vector3.up * 0.2f, 3f, new Color(1f, 0.5f, 0.15f), 0.12f);
                        break;
                    case GameEventKind.Pickup:
                        for (int i = 0; i < 14; i++)
                            Spark(at, new Vector3(Random.Range(-2f, 2f), Random.Range(3f, 7f), Random.Range(-2f, 2f)), new Color(1f, 0.85f, 0.4f),
                                0.6f, 0.15f, gravity: 4f);
                        break;
                }
            }
        }

        public static Color WeaponColor(WeaponKind kind) => kind switch
        {
            WeaponKind.ArcaneWand => new Color(0.55f, 0.85f, 1f),
            WeaponKind.Minigun => new Color(1f, 0.7f, 0.25f),
            WeaponKind.TdMortar => new Color(1f, 0.45f, 0.12f),
            _ => new Color(0.7f, 0.35f, 1f),
        };

        // --- particles ---------------------------------------------------------------------------

        /// <summary>A short-lived flash: a big dot that fades out fast.</summary>
        public void Flash(Vector3 at, float size, Color color, float life = 0.1f) =>
            Add(new Particle { Position = at, Color = color, Life = life, MaxLife = life, Size = size, Drag = 0f });

        public void Spark(Vector3 at, Vector3 velocity, Color color, float life, float size, float gravity = 0f, float stretch = 0.035f, float drag = 3f) =>
            Add(new Particle { Position = at, Velocity = velocity, Color = color, Life = life, MaxLife = life, Size = size, Stretch = stretch, Drag = drag, Gravity = gravity });

        private void Add(in Particle particle)
        {
            if (_count == MaxParticles)
                return; // full: drop it rather than stall
            _particles[_count++] = particle;
        }

        private void UpdateParticles(float dt)
        {
            for (int i = 0; i < _count; i++)
            {
                ref var p = ref _particles[i];
                p.Life -= dt;
                if (p.Life <= 0f)
                {
                    _particles[i--] = _particles[--_count];
                    continue;
                }
                p.Velocity *= Mathf.Exp(-p.Drag * dt);
                p.Velocity.y -= p.Gravity * dt;
                p.Position += p.Velocity * dt;

                float t = p.Life / p.MaxLife;
                var color = p.Color;
                color.a *= t;
                float size = p.Stretch > 0f ? p.Size : p.Size * (0.6f + 0.4f * t);
                if (p.Stretch > 0f && p.Velocity.sqrMagnitude > 0.01f)
                    Streak(p.Position, p.Velocity, size + p.Velocity.magnitude * p.Stretch * 2f, size, color);
                else
                    Dot(p.Position, size * (p.MaxLife <= 0.15f ? 1f + (1f - t) : 1f), color);
            }
        }

        /// <summary>A dot stretched along the screen direction of <paramref name="velocity"/>.</summary>
        private void Streak(Vector3 at, Vector3 velocity, float length, float width, Color color)
        {
            var along = velocity - Vector3.Dot(velocity, _forward) * _forward;
            if (along.sqrMagnitude < 1e-6f)
            {
                Dot(at, width, color);
                return;
            }
            along.Normalize();
            var across = Vector3.Cross(_forward, along);
            _dots.Billboard(at, along, across, length, width, color);
        }
    }
}
