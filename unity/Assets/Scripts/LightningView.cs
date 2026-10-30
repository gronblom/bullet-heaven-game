using System.Collections.Generic;
using BulletHeaven.Ecs;
using UnityEngine;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// Lightning: jagged flickering bolts (two lines each, glow + hot core) for the wand's zaps, and a
    /// real point light on each ball-lightning orb (the closest few), so they light up the swarm.
    /// </summary>
    public sealed class LightningView
    {
        public const float ArcLifetime = 0.18f;
        private const int MaxOrbLights = 8;
        private const int Segments = 9;

        private readonly Transform _parent;
        private readonly Material _material;
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private readonly List<Light> _lights = new List<Light>();
        private readonly Vector3[] _points = new Vector3[Segments + 1];

        public LightningView(Transform parent)
        {
            _parent = parent;
            _material = Looks.Additive(Color.white);
            _material.SetFloat("_UseVertexColor", 1f);
        }

        public void Update(List<LightningArc> arcs, List<Vector3> orbs, Vector3 camera, float time, float dt)
        {
            int line = 0;
            for (int i = arcs.Count - 1; i >= 0; i--)
            {
                var arc = arcs[i];
                arc.Age += dt;
                if (arc.Age > ArcLifetime)
                {
                    arcs.RemoveAt(i);
                    continue;
                }
                float fade = 1f - arc.Age / ArcLifetime;
                var from = arc.From.ToUnity();
                var to = arc.To.ToUnity();
                Jag(from, to, arc.Seed + (int)(time * 30f));
                Line(line++, 0.32f, new Color(0.45f, 0.7f, 1f, fade));
                Line(line++, 0.11f, new Color(0.95f, 0.98f, 1f, fade));
            }
            for (int i = line; i < _lines.Count; i++)
                _lines[i].enabled = false;

            orbs.Sort((a, b) => (a - camera).sqrMagnitude.CompareTo((b - camera).sqrMagnitude));
            for (int i = 0; i < MaxOrbLights; i++)
            {
                if (i == _lights.Count)
                {
                    var go = new GameObject("OrbLight");
                    go.transform.SetParent(_parent, false);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(0.55f, 0.8f, 1f);
                    light.range = 7f;
                    _lights.Add(light);
                }
                bool on = i < orbs.Count;
                _lights[i].enabled = on;
                if (on)
                {
                    _lights[i].transform.position = orbs[i];
                    _lights[i].intensity = 3f + Mathf.Sin(time * 40f + i) * 0.8f;
                }
            }
        }

        private void Jag(Vector3 from, Vector3 to, int seed)
        {
            var random = new System.Random(seed);
            var along = to - from;
            var side = Vector3.Cross(along, Vector3.up).normalized;
            if (side == Vector3.zero)
                side = Vector3.right;
            float jitter = along.magnitude * 0.12f;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                float edge = Mathf.Sin(t * Mathf.PI); // pinned at both ends
                float sideways = ((float)random.NextDouble() - 0.5f) * 2f * jitter * edge;
                float up = ((float)random.NextDouble() - 0.5f) * jitter * edge;
                _points[i] = from + along * t + side * sideways + Vector3.up * up;
            }
        }

        private void Line(int index, float width, Color color)
        {
            if (index == _lines.Count)
            {
                var go = new GameObject("Bolt");
                go.transform.SetParent(_parent, false);
                var renderer = go.AddComponent<LineRenderer>();
                renderer.sharedMaterial = _material;
                renderer.positionCount = Segments + 1;
                renderer.useWorldSpace = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _lines.Add(renderer);
            }
            var line = _lines[index];
            line.enabled = true;
            line.widthMultiplier = width;
            line.startColor = line.endColor = color;
            line.SetPositions(_points);
        }
    }
}
