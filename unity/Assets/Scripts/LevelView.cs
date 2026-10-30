using System.Collections.Generic;
using BulletHeaven.Levels;
using UnityEngine;
using Core = BulletHeaven.Numerics;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// Builds a level's <see cref="LevelLayout"/> (plain data from the shared core) as Unity objects:
    /// generated models where they exist, primitive fallbacks otherwise, plus floor, walls and lights.
    /// Same rules as the Godot LevelView, so both versions look laid out the same.
    /// </summary>
    public static class LevelView
    {
        public static void Build(Transform parent, Level level, LevelLayout layout, OcclusionFader fader)
        {
            if (layout.Floor != null)
                Floor(parent, level.Bounds, layout.Floor);
            if (layout.Walls != null)
                Walls(parent, level.Bounds, layout.Walls);
            foreach (var prop in layout.Props)
                Prop(parent, prop, fader);
            foreach (var plank in layout.Planks)
                Plank(parent, plank);
            foreach (var light in layout.Lights)
                Light(parent, light);
        }

        private static void Prop(Transform parent, PropVisual prop, OcclusionFader fader)
        {
            var holder = new GameObject(prop.Model ?? prop.Fallback.ToString()).transform;
            holder.SetParent(parent, false);
            holder.localPosition = prop.Position.ToUnity();
            holder.localRotation = Conv.Yaw(prop.YawDegrees);

            var materials = new List<Material>();
            var model = prop.Model == null ? null
                : ModelLibrary.LoadProp(prop.Model, prop.Size?.Size(), prop.Tint?.ToUnity(), materials);
            if (model != null)
            {
                model.transform.SetParent(holder, false);
            }
            else if (prop.Fallback != Shape.None)
            {
                var material = Looks.FromLook(prop.FallbackLook);
                var size = prop.FallbackSize.Size();
                var mesh = Primitive(prop.Fallback, prop.TopScale);
                var go = MeshObject(prop.Fallback.ToString(), mesh, material, holder, prop.CastShadows);
                if (prop.Fallback == Shape.Plane)
                {
                    go.transform.localScale = new Vector3(size.x, 1f, size.z);
                }
                else
                {
                    go.transform.localPosition = new Vector3(0f, size.y / 2f, 0f);
                    go.transform.localScale = size;
                }
                materials.Add(material);
            }

            if (materials.Count > 0 && prop.Fade is Core.Rect2 fade)
                fader?.Register(fade, prop.FadeTop, materials);
        }

        private static void Plank(Transform parent, PlankVisual plank)
        {
            // Turn to face the direction, then tilt the long board up towards its local +X.
            var pivot = new GameObject(plank.Model).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = plank.Center.ToUnity();
            pivot.localRotation = Conv.Yaw(plank.YawDegrees) * Quaternion.Euler(0f, 0f, plank.TiltRadians * Mathf.Rad2Deg);
            var size = plank.BoardSize.Size();
            var model = ModelLibrary.LoadProp(plank.Model, size);
            if (model != null)
            {
                model.transform.SetParent(pivot, false);
                model.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            }
            else
            {
                var go = MeshObject("board", Meshes.Cube, Looks.FromLook(plank.FallbackLook), pivot, true);
                go.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                go.transform.localScale = size;
            }
        }

        private static void Light(Transform parent, LightVisual visual)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = visual.Position.ToUnity();
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = visual.Color.ToUnity();
            light.intensity = visual.Energy * 1.5f;
            light.range = visual.Range;
            light.shadows = visual.Shadows ? LightShadows.Soft : LightShadows.None;
            if (visual.Flicker)
                go.AddComponent<FlickerLight>();
        }

        private static Mesh Primitive(Shape shape, float topScale) => shape switch
        {
            Shape.Sphere => Meshes.Sphere,
            Shape.Cylinder => Meshes.Cylinder(topScale),
            Shape.Wedge => Meshes.Wedge,
            Shape.Plane => Meshes.Plane,
            _ => Meshes.Cube,
        };

        public static GameObject MeshObject(string name, Mesh mesh, Material material, Transform parent, bool castShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>The floor: a baked texture set tiled every 16 units, or a flat colour.</summary>
        private static void Floor(Transform parent, Core.Rect2 bounds, FloorVisual floor)
        {
            Material material;
            var albedo = ModelLibrary.LoadTexture(floor.TextureSet + "_albedo");
            if (albedo != null)
            {
                material = Looks.Plastic(Color.white, 1f);
                material.SetTexture("_BaseMap", albedo);
                material.SetTextureScale("_BaseMap", new Vector2(bounds.Size.X / 16f, bounds.Size.Y / 16f));
                var roughness = ModelLibrary.LoadTexture(floor.TextureSet + "_roughness");
                if (roughness != null)
                    material.SetTexture("_OrmMap", roughness); // greyscale: G = roughness, metallic stays 0
                var normal = ModelLibrary.LoadTexture(floor.TextureSet + "_normal");
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_HasNormalMap", 1f);
                }
            }
            else
            {
                material = Looks.Plastic(floor.Fallback.ToUnity(), 0.6f);
            }
            var center = bounds.GetCenter();
            var go = MeshObject("Floor", Meshes.Plane, material, parent, false);
            // A hair below zero: props stand at exactly 0, and coplanar faces flicker (z-fighting).
            go.transform.localPosition = Conv.World(center, -0.02f);
            go.transform.localScale = new Vector3(bounds.Size.X, 1f, bounds.Size.Y);
        }

        /// <summary>Back and side walls just outside the play area (or wall panels), plus a low skirting at the front.</summary>
        private static void Walls(Transform parent, Core.Rect2 b, WallsVisual walls)
        {
            var material = Looks.FromLook(walls.Look);
            var center = b.GetCenter();
            void Box(Vector3 size, Core.Vector2 at, float y)
            {
                var go = MeshObject("Wall", Meshes.Cube, material, parent, true);
                go.transform.localPosition = Conv.World(at, y);
                go.transform.localScale = size;
            }

            if (walls.PanelModel != null && ModelLibrary.Prefab(walls.PanelModel) != null)
            {
                void Panel(Core.Vector2 at, float godotYaw)
                {
                    var panel = ModelLibrary.LoadProp(walls.PanelModel);
                    panel.transform.SetParent(parent, false);
                    panel.transform.localPosition = Conv.World(at, 0f);
                    panel.transform.localRotation = Conv.Yaw(godotYaw);
                }
                for (float x = b.Position.X + 6f; x < b.End.X; x += walls.PanelSpacing)
                    Panel(new Core.Vector2(x, b.Position.Y - 1f), 0f);
                for (float y = b.Position.Y + 8f; y < b.End.Y; y += walls.PanelSpacing)
                {
                    Panel(new Core.Vector2(b.Position.X - 1f, y), 90f);
                    Panel(new Core.Vector2(b.End.X + 1f, y), -90f);
                }
            }
            else
            {
                float w = b.Size.X + 4f, d = b.Size.Y + 4f, h = walls.Height;
                Box(new Vector3(w, h, 2f), new Core.Vector2(center.X, b.Position.Y - 1f), h / 2f);
                Box(new Vector3(2f, h, d), new Core.Vector2(b.Position.X - 1f, center.Y), h / 2f);
                Box(new Vector3(2f, h, d), new Core.Vector2(b.End.X + 1f, center.Y), h / 2f);
            }
            Box(new Vector3(b.Size.X + 4f, 1.2f, 1f), new Core.Vector2(center.X, b.End.Y + 0.5f), 0.6f);
        }

        /// <summary>Default daylight, then the level's mood on top (null fields keep the default).</summary>
        public static void Atmosphere(Light sun, Material sky, Atmosphere mood)
        {
            var skyTop = new Color(0.45f, 0.58f, 0.78f);
            var horizon = new Color(0.8f, 0.72f, 0.6f);
            var groundBottom = new Color(0.3f, 0.27f, 0.24f);
            float ambient = 0.45f;
            var sunColor = new Color(1f, 0.9f, 0.75f);
            float sunEnergy = 1.6f;
            var sunRotation = new Vector3(-55f, 35f, 0f);
            RenderSettings.fog = false;

            if (mood != null)
            {
                if (mood.SkyTop is Core.Color top) skyTop = top.ToUnity();
                if (mood.SkyHorizon is Core.Color h) horizon = h.ToUnity();
                if (mood.GroundBottom is Core.Color g) groundBottom = g.ToUnity();
                if (mood.AmbientEnergy is float a) ambient = a;
                if (mood.SunColor is Core.Color c) sunColor = c.ToUnity();
                if (mood.SunEnergy is float e) sunEnergy = e;
                if (mood.SunRotationDegrees is Core.Vector3 r) sunRotation = new Vector3(r.X, r.Y, r.Z);
                // URP has no volumetric fog; plain exponential fog in the same colour stands in for it.
                float? fogDensity = mood.FogDensity ?? (mood.VolumetricFogDensity * 1.5f);
                if (fogDensity is float density)
                {
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogDensity = density;
                    RenderSettings.fogColor = (mood.FogDensity != null ? mood.FogColor : mood.VolumetricFogAlbedo).ToUnity() * horizon;
                }
            }

            float light = ambient * 1.8f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = skyTop * light;
            RenderSettings.ambientEquatorColor = horizon * light;
            RenderSettings.ambientGroundColor = groundBottom * light;
            if (sky != null)
            {
                sky.SetColor("_SkyTint", skyTop);
                sky.SetColor("_GroundColor", groundBottom);
                RenderSettings.skybox = sky;
            }

            sun.color = sunColor;
            sun.intensity = sunEnergy;
            // Godot rotation (YXZ Euler, light shines along its -Z) to a Unity direction (z flipped).
            float p = sunRotation.x * Mathf.Deg2Rad, y = sunRotation.y * Mathf.Deg2Rad;
            var direction = new Vector3(-Mathf.Cos(p) * Mathf.Sin(y), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
            sun.transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    /// <summary>
    /// Fades tall props to see-through (dithered) while they stand between the camera and the player,
    /// so the player never disappears behind the counter. Works in the core's ground-plane coordinates,
    /// with the same footprints and top heights as the collision.
    /// </summary>
    public sealed class OcclusionFader
    {
        private const int Samples = 16;
        private const float FadedAlpha = 0.3f;
        private const float SmallFadedAlpha = 0.55f;
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private sealed class Occluder
        {
            public Core.Rect2 Footprint;
            public float Top;
            public Material[] Materials;
            public float Alpha = 1f;
        }

        private readonly List<Occluder> _occluders = new List<Occluder>();

        public void Register(Core.Rect2 footprint, float top, List<Material> materials) =>
            _occluders.Add(new Occluder { Footprint = footprint, Top = top, Materials = materials.ToArray() });

        public void Update(Vector3 camera, Vector3 target, float dt)
        {
            float blend = 1f - Mathf.Exp(-10f * dt);
            foreach (var occluder in _occluders)
            {
                bool small = occluder.Footprint.Size.X * occluder.Footprint.Size.Y < 30f;
                float goal = Blocks(occluder, camera, target) ? (small ? SmallFadedAlpha : FadedAlpha) : 1f;
                if (goal == 1f && occluder.Alpha == 1f)
                    continue;
                occluder.Alpha = Mathf.Lerp(occluder.Alpha, goal, blend);
                if (Mathf.Abs(occluder.Alpha - goal) < 0.01f)
                    occluder.Alpha = goal;
                foreach (var material in occluder.Materials)
                    material.SetFloat(FadeId, occluder.Alpha);
            }
        }

        private static bool Blocks(Occluder occluder, Vector3 camera, Vector3 target)
        {
            for (int i = 1; i < Samples; i++)
            {
                var p = Vector3.Lerp(target, camera, i / (float)Samples).ToCore();
                if (p.Y < occluder.Top && occluder.Footprint.HasPoint(new Core.Vector2(p.X, p.Z)))
                    return true;
            }
            return false;
        }
    }

    /// <summary>A light that buzzes: now and then it dips for a moment, like a candle.</summary>
    public sealed class FlickerLight : MonoBehaviour
    {
        private Light _light;
        private float _base;
        private float _timer;

        private void Start()
        {
            _light = GetComponent<Light>();
            _base = _light.intensity;
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            if (Random.value < 0.08f)
            {
                _light.intensity = _base * Random.value * 0.3f;
                _timer = 0.03f + Random.value * 0.12f;
            }
            else
            {
                _light.intensity = _base * (0.92f + Random.value * 0.08f);
                _timer = 0.05f + Random.value * 0.6f;
            }
        }
    }
}
