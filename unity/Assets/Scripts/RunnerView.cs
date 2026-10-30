using System.Collections.Generic;
using BulletHeaven.Ecs;
using UnityEngine;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// The little explorer: the generated runner model (or a primitive dude), with a procedural run
    /// cycle on its LegL/LegR/ArmL/ArmR parts, a jump pose, a squash on landing, carried weapons and
    /// the parachute canopy. Port of the Godot RunnerView.
    /// </summary>
    public sealed class RunnerView : MonoBehaviour
    {
        private readonly Transform[] _legs = new Transform[2];
        private readonly Transform[] _arms = new Transform[2];
        private Quaternion[] _legRest = new Quaternion[2];
        private Quaternion[] _armRest = new Quaternion[2];
        private Transform _body;
        private bool _modelLimbs;
        private float _phase;
        private float _squash;
        private GameObject _minigun;
        private GameObject _wand;
        private Transform _barrels;
        private float _barrelAngle;
        private Transform _canopy;
        private float _canopyOpen;
        private Renderer[] _renderers;

        public static RunnerView Create(Transform parent)
        {
            var go = new GameObject("Runner");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RunnerView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            var model = ModelLibrary.LoadScene(ModelLibrary.Runner);
            if (model != null)
            {
                model.transform.SetParent(_body, false);
                _legs[0] = ModelLibrary.FindNode(model.transform, "LegL");
                _legs[1] = ModelLibrary.FindNode(model.transform, "LegR");
                _arms[0] = ModelLibrary.FindNode(model.transform, "ArmL");
                _arms[1] = ModelLibrary.FindNode(model.transform, "ArmR");
                _modelLimbs = _legs[0] != null && _legs[1] != null && _arms[0] != null && _arms[1] != null;
                if (_modelLimbs)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        _legRest[i] = _legs[i].localRotation;
                        _armRest[i] = _arms[i].localRotation;
                    }
                }
            }
            else
            {
                // Placeholder: big head, short body, facing +X.
                var suit = Looks.Plastic(new Color(0.95f, 0.45f, 0.1f));
                var skin = Looks.Plastic(new Color(1f, 0.85f, 0.7f), 0.5f);
                Part(Meshes.Cylinder(), suit, new Vector3(0f, 0.45f, 0f), new Vector3(0.4f, 0.5f, 0.4f));
                Part(Meshes.Sphere, skin, new Vector3(0f, 0.9f, 0f), Vector3.one * 0.44f);
            }
            _renderers = GetComponentsInChildren<Renderer>();
        }

        private void Part(Mesh mesh, Material material, Vector3 at, Vector3 scale)
        {
            var go = LevelView.MeshObject("part", mesh, material, _body, true);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
        }

        /// <summary>Shows the weapons being carried (minigun in the right hand, wand in the left).</summary>
        public void ShowWeapons(bool minigun, bool wand)
        {
            if (minigun && _minigun == null)
            {
                _minigun = Hold("minigun", 1);
                if (_minigun != null)
                    _barrels = ModelLibrary.FindNode(_minigun.transform, "Barrels");
            }
            if (wand && _wand == null)
                _wand = Hold("wand", 0);
            if (_minigun != null)
                _minigun.SetActive(minigun);
            if (_wand != null)
                _wand.SetActive(wand);
        }

        private GameObject Hold(string name, int arm)
        {
            if (_arms[arm] == null)
                return null;
            // Inside the runner model (already mirrored like the weapon), so no extra turn.
            var weapon = ModelLibrary.LoadProp(name, inModelSpace: true);
            if (weapon == null)
                return null;
            weapon.transform.SetParent(_arms[arm], false);
            // Hand position relative to the shoulder pivot in the model's frame (Godot's, mirrored on X).
            float side = arm == 0 ? 1f : -1f;
            weapon.transform.localPosition = new Vector3(-side * 0.06f, -0.35f, 0.03f);
            _renderers = GetComponentsInChildren<Renderer>(true);
            return weapon;
        }

        public void Sync(in Position position, in Velocity velocity, in Runner runner, in Elevation elevation, in Health health,
            float minigunSpin, float dt)
        {
            transform.localPosition = Conv.World(position.Value, elevation.Height);
            transform.localRotation = Quaternion.Euler(0f, Conv.Heading(runner.Facing), 0f);

            float speed = velocity.Value.Length();
            float run = elevation.Grounded ? Mathf.Clamp01(speed / 7.5f) : 0f;
            _phase += dt * (6f + speed * 1.6f) * (run > 0.05f ? 1f : 0f);
            float swing = Mathf.Sin(_phase) * 0.9f * run;

            if (_modelLimbs)
            {
                // Limbs swing about their local X in the model's frame.
                float airLegs = elevation.Grounded ? 0f : -0.6f;
                float airArms = elevation.Grounded ? 0f : -1.4f;
                Swing(_legs[0], _legRest[0], swing + airLegs);
                Swing(_legs[1], _legRest[1], -swing + airLegs * 0.5f);
                Swing(_arms[0], _armRest[0], _wand != null && _wand.activeSelf ? -0.35f - swing * 0.1f : -swing * 0.8f + airArms);
                Swing(_arms[1], _armRest[1], _minigun != null && _minigun.activeSelf ? -0.3f + swing * 0.1f : swing * 0.8f + airArms);
            }

            if (_barrels != null)
            {
                _barrelAngle += minigunSpin * 35f * dt;
                _barrels.localRotation = Quaternion.Euler(0f, 0f, -_barrelAngle * Mathf.Rad2Deg);
            }

            // Bob while running, lean into speed, squash on landing.
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.08f * run;
            if (elevation.LandingImpact > 2f)
                _squash = Mathf.Min(0.4f, elevation.LandingImpact * 0.03f);
            _squash = Mathf.Max(0f, _squash - dt * 2f);
            _body.localPosition = new Vector3(0f, bob, 0f);
            _body.localRotation = Quaternion.Euler(0f, 0f, -run * 0.2f * Mathf.Rad2Deg);
            _body.localScale = new Vector3(1f + _squash * 0.5f, 1f - _squash, 1f + _squash * 0.5f);

            // Blink while invulnerable.
            bool visible = health.Invulnerable <= 0f || (int)(health.Invulnerable * 20f) % 2 == 0;
            foreach (var renderer in _renderers)
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        private static void Swing(Transform limb, Quaternion rest, float radians) =>
            limb.localRotation = rest * Quaternion.Euler(radians * Mathf.Rad2Deg, 0f, 0f);

        /// <summary>Opens the parachute canopy over the shoulders while gliding, folding it away on landing.</summary>
        public void SyncParachute(bool gliding, float dt)
        {
            if (gliding && _canopy == null)
            {
                _canopy = new GameObject("Harness").transform;
                _canopy.SetParent(transform, false);
                _canopy.localPosition = new Vector3(0f, 0.85f, 0f);
                var model = ModelLibrary.LoadProp("parachute");
                if (model != null)
                {
                    model.transform.SetParent(_canopy, false);
                }
                else
                {
                    var dome = LevelView.MeshObject("canopy", Meshes.Sphere, Looks.Plastic(new Color(0.9f, 0.35f, 0.05f), 0.7f), _canopy, true);
                    dome.transform.localPosition = new Vector3(0f, 1.4f, 0f);
                    dome.transform.localScale = new Vector3(1.8f, 0.9f, 1.8f);
                }
            }
            if (_canopy == null)
                return;
            _canopyOpen = Mathf.MoveTowards(_canopyOpen, gliding ? 1f : 0f, dt * (gliding ? 4f : 6f));
            _canopy.gameObject.SetActive(_canopyOpen > 0.02f);
            // Pops open wide first, and sways a little.
            float open = 1f - Mathf.Pow(1f - _canopyOpen, 2.5f);
            _canopy.localScale = new Vector3(open, 0.3f + 0.7f * open, open);
            _canopy.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 2f) * 4.5f, 0f, 5.7f * open);
        }
    }
}
