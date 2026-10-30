using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Levels;
using BulletHeaven.Systems;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CoreTerrain = BulletHeaven.Ecs.Terrain;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// Unity front-end for the shared gameplay core, the counterpart of the Godot project's Main.
    /// It builds the level and the ECS world, fills in the controls, runs the systems in Main's
    /// order and draws the result (instanced bugs and projectiles, the runner, the level). Menus and
    /// the HUD are in <see cref="GameUi"/>. It creates itself in whatever scene is open: just press Play.
    /// </summary>
    public sealed class GameRunner : MonoBehaviour
    {
        /// <summary>Level picked with F1-F3 or reached through a secret exit.</summary>
        private static int _selectedLevel;
        /// <summary>Weapons carried through a secret exit into the next level (skips the loadout menu once).</summary>
        private static WeaponKind[] _carriedLoadout;

        private static readonly Vector3 CameraOffset = new Vector3(0f, 32f, -18f); // Godot's (0, 32, 18), z flipped
        private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Velocity, Radius, Elevation>();
        private static readonly QueryDescription BulletQuery = new QueryDescription().WithAll<Bullet, Position, Velocity>();
        private static readonly QueryDescription GemQuery = new QueryDescription().WithAll<XpGem, Position>();
        private static readonly QueryDescription OrbQuery = new QueryDescription().WithAll<LightningOrb, Position>();
        private static readonly QueryDescription ShellQuery = new QueryDescription().WithAll<Shell, Position>();
        private static readonly QueryDescription BlastQuery = new QueryDescription().WithAll<Blast, Position>();
        private static readonly QueryDescription PuddleQuery = new QueryDescription().WithAll<Puddle, Position>();
        private static readonly QueryDescription PickupQuery = new QueryDescription().WithAll<Pickup, Position>();
    private static readonly QueryDescription PuffQuery = new QueryDescription().WithAll<SprayPuff, Position>();

        private World _world;
        private GameState _state;
        private Level _level;
        private GameSystem[] _systems;
        private Transform _root;
        private RunnerView _runner;
        private OcclusionFader _fader;
        private Camera _camera;
        private Light _sun;
        private Volume _volume;
        private DepthOfField _depthOfField;
        private Material _sky;
        private Vector3 _cameraTarget;
        private GameUi _ui;
        private InstancedBatch _ants, _beetles, _bullets, _gems, _orbs, _shells, _blasts, _puddles, _puffs;
        private LightningView _lightning;
        private readonly List<Vector3> _orbPositions = new List<Vector3>();
        private readonly Dictionary<Entity, Transform> _pickupViews = new Dictionary<Entity, Transform>();
        private bool _choosing; // a loadout or level-up menu is open (pause can't close it)

        public GameState State => _state;
        public Level Level => _level;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<GameRunner>() == null)
                new GameObject("Bullet Heaven").AddComponent<GameRunner>();
        }

        private void Start()
        {
            // This runner brings its own camera, sun and post-processing; switch off the scene's.
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                camera.gameObject.SetActive(false);
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                light.gameObject.SetActive(false);
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                volume.gameObject.SetActive(false);

            BuildCameraAndLight();
            _ui = gameObject.AddComponent<GameUi>();
            _ui.Runner = this;
            Begin(_selectedLevel);
        }

        private void OnDestroy() => Teardown();

        // --- run lifecycle ---------------------------------------------------------------------

        private void Begin(int levelIndex)
        {
            _selectedLevel = Mathf.Clamp(levelIndex, 0, Level.All.Length - 1);
            _level = Level.All[_selectedLevel];
            _world = World.Create();
            _state = new GameState(_level.Bounds);
            _root = new GameObject(_level.Name).transform;
            _fader = new OcclusionFader();

            var layout = _level.Build(_world);
            LevelView.Build(_root, _level, layout, _fader);
            LevelView.Atmosphere(_sun, _sky, _level.Atmosphere);
            _state.Terrain = CoreTerrain.FromWorld(_world, _level.Bounds);
            _state.ExitArea = _level.Exit;
            _state.ExitHeight = _level.ExitHeight;
            if (_level.BossSpawn is BulletHeaven.Numerics.Vector2 bossSpawn)
            {
                _state.BossLevel = true;
                _state.BossSpawn = bossSpawn;
                _state.SpawnRateScale = 0.6f; // the queen brings her own ants
            }

            var start = _level.PlayerStart;
            _state.Player = _world.Create(
                new PlayerTag(), new Position(start), new Velocity(), new Health(100f),
                new Elevation { Grounded = true }, new Radius(0.35f),
                new Runner { Facing = -Mathf.PI / 2f }, RunnerStats.Default);
            _runner = RunnerView.Create(_root);

            BuildBatches();
            _lightning = new LightningView(_root);
            _pickupViews.Clear();
            _world.Query(in PickupQuery, (Entity entity, ref Pickup pickup, ref Position at) =>
            {
                var view = ModelLibrary.LoadProp("parachute");
                if (view == null)
                {
                    view = LevelView.MeshObject("parachute", Meshes.Sphere, Looks.Plastic(new Color(0.9f, 0.35f, 0.05f), 0.7f), _root, true);
                    view.transform.localScale = new Vector3(1.8f, 0.9f, 1.8f);
                }
                view.transform.SetParent(_root, false);
                _pickupViews[entity] = view.transform;
            });

            // Order matters (same as the Godot Main): input -> physics -> collisions -> spatial index -> combat -> cleanup.
            _systems = new GameSystem[]
            {
                new PlayerInputSystem(_world, _state),
                new RunnerPhysicsSystem(_world, _state),
                new CarPhysicsSystem(_world, _state),
                new EnemySteeringSystem(_world, _state),
                new BossSystem(_world, _state),
                new LiquidSystem(_world, _state),
                new SpraySystem(_world, _state),
                new MovementSystem(_world, _state),
                new CollisionSystem(_world, _state),
                new VerticalPhysicsSystem(_world, _state),
                new ItemSystem(_world, _state),
                new EnemyGridSystem(_world, _state),
                new PlayerContactSystem(_world, _state),
                new WeaponSystem(_world, _state),
                new BulletSystem(_world, _state),
                new LightningOrbSystem(_world, _state),
                new ShellSystem(_world, _state),
                new EnemyDeathSystem(_world, _state),
                new PickupSystem(_world, _state),
                new EnemySpawnSystem(_world, _state),
            };

            _cameraTarget = Conv.World(start, 0f);
            _choosing = false;
            _ui.Close();
            if (_carriedLoadout != null)
            {
                foreach (var kind in _carriedLoadout)
                    Arsenal.Grant(_world, kind);
                _carriedLoadout = null;
                StartRun();
            }
            else
            {
                ChooseLoadout();
            }
        }

        private void Teardown()
        {
            if (_root != null)
                Destroy(_root.gameObject);
            if (_world != null)
                World.Destroy(_world);
            _world = null;
        }

        public void Restart(int levelIndex)
        {
            Teardown();
            Begin(levelIndex);
        }

        private void StartRun() =>
            _state.ShowMessage($"{_level.Name}\nWASD / arrows / stick to run, Space / A to jump\nEsc / Start: pause   F1-F3: switch level", 5f);

        /// <summary>Run start: pick two of the four weapons, one card at a time.</summary>
        private void ChooseLoadout()
        {
            _choosing = true;
            var kinds = new List<WeaponKind>(Cards.Loadout);
            void Offer(string title)
            {
                _ui.ShowCards(title, "1-4 / click / stick + A", Cards.Weapons(kinds), i =>
                {
                    Arsenal.Grant(_world, kinds[i]);
                    kinds.RemoveAt(i);
                    if (Arsenal.Carried(_world).Count < Cards.LoadoutPicks)
                    {
                        Offer("PICK YOUR SECOND WEAPON");
                        return;
                    }
                    _choosing = false;
                    StartRun();
                });
            }
            Offer("PICK YOUR FIRST WEAPON");
        }

        /// <summary>Level-up: pause and offer three modifiers.</summary>
        private void ChooseModifier()
        {
            _state.PendingLevelUps--;
            var offers = Modifiers.Roll(_world, _state, 3);
            if (offers.Count == 0)
            {
                ref var health = ref _world.Get<Health>(_state.Player);
                health.Current = health.Max; // everything maxed: have a heal instead
                _state.ShowMessage($"LEVEL {_state.Level}: fully healed");
                return;
            }
            _choosing = true;
            var cards = offers.ConvertAll(m => Cards.Modifier(_state, m));
            _ui.ShowCards($"LEVEL {_state.Level}", "Pick one: 1-3 / click / stick + A", cards, i =>
            {
                Modifiers.Pick(_world, _state, offers[i]);
                _choosing = false;
            });
        }

        private void TogglePause()
        {
            if (_ui.IsOpen)
            {
                _ui.Close();
                return;
            }
            _ui.ShowCards("PAUSED", "Esc / Start to resume", Cards.Pause(), i =>
            {
                if (i == 1)
                    Restart(_selectedLevel);
                else if (i == 2)
                    Restart(0);
            });
        }

        /// <summary>Through the secret exit: remember it's unlocked and carry on with the same weapons.</summary>
        private void TakeExit()
        {
            PlayerPrefs.SetInt("unlocked." + Level.All[_level.ExitTo].Name, 1);
            PlayerPrefs.Save();
            _carriedLoadout = Arsenal.Carried(_world).ConvertAll(w => w.Kind).ToArray();
            Restart(_level.ExitTo);
        }

        // --- frame -----------------------------------------------------------------------------

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f); // avoid huge steps after hitches

            for (int i = 0; i < Level.All.Length && i < 3; i++)
            {
                if (Input.GetKeyDown(KeyCode.F1 + i))
                {
                    Restart(i);
                    return;
                }
            }

            bool pause = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.JoystickButton7);
            if (_state.GameOver)
            {
                if (Input.GetKeyDown(KeyCode.R) || pause)
                {
                    Restart(_selectedLevel);
                    return;
                }
            }
            else if (pause && !_choosing)
            {
                TogglePause();
            }
            else if (!_ui.IsOpen && _state.PendingLevelUps > 0)
            {
                ChooseModifier();
            }
            else if (!_ui.IsOpen && _state.ReachedExit)
            {
                TakeExit();
                return;
            }
            else if (!_ui.IsOpen)
            {
                DebugWeaponKeys();
                ReadInput();
                _state.Time += dt;
                foreach (var system in _systems)
                    system.Update(dt);
                _state.FlushDestroyed(_world);
            }

            if (_world != null)
                SyncVisuals(dt);
        }

        /// <summary>Legacy input manager to the engine-independent controls (screen up = -Y on the ground plane).</summary>
        private void ReadInput()
        {
            var move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 1f)
                move.Normalize();
            bool jumpPressed = Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.JoystickButton0);
            bool jumpHeld = Input.GetButton("Jump") || Input.GetKey(KeyCode.JoystickButton0);
            _state.Input = new PlayerInput
            {
                Move = new BulletHeaven.Numerics.Vector2(move.x, -move.y),
                JumpPressed = jumpPressed,
                JumpHeld = jumpHeld,
            };
        }

        /// <summary>Shift + 1-3 adds a weapon, for trying them out.</summary>
        private void DebugWeaponKeys()
        {
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                return;
            for (int i = 0; i < 3; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    _state.ShowMessage(Arsenal.Grant(_world, (WeaponKind)i), 1.5f);
            }
        }

        private void SyncVisuals(float dt)
        {
            var player = _state.Player;
            var position = _world.Get<Position>(player);
            var velocity = _world.Get<Velocity>(player);
            var elevation = _world.Get<Elevation>(player);
            var health = _world.Get<Health>(player);
            var runner = _world.Get<Runner>(player);
            var weapons = Arsenal.Carried(_world);
            float minigunSpin = weapons.Find(w => w.Kind == WeaponKind.Minigun).Spin; // default (0) if not carried

            _runner.ShowWeapons(weapons);
            _runner.Sync(position, velocity, runner, elevation, health, minigunSpin, dt);
            _runner.SyncParachute(runner.Gliding, dt);

            // Camera never rotates; it trails the player with some look-ahead and follows them up and down.
            var lookAhead = new Vector3(velocity.Value.X, 0f, -velocity.Value.Y) * 0.3f;
            var desired = Conv.World(position.Value, elevation.Height) + lookAhead;
            _cameraTarget.x = Mathf.Lerp(_cameraTarget.x, desired.x, 1f - Mathf.Exp(-4f * dt));
            _cameraTarget.z = Mathf.Lerp(_cameraTarget.z, desired.z, 1f - Mathf.Exp(-4f * dt));
            _cameraTarget.y = Mathf.Lerp(_cameraTarget.y, desired.y, 1f - Mathf.Exp(-2.5f * dt));
            _camera.transform.position = _cameraTarget + CameraOffset;
            _camera.transform.LookAt(_cameraTarget);
            _depthOfField.focusDistance.value = CameraOffset.magnitude;

            var playerPoint = Conv.World(position.Value, elevation.Height + 0.5f);
            _fader.Update(_camera.transform.position, playerPoint, dt);

            DrawBugs();
            DrawProjectiles(dt);
            DrawWorldItems();
            _ui.EnemyCount = _ants.Count + _beetles.Count;
            _ui.Speed = velocity.Value.Length();
            _ui.Health = health;
            _ui.Weapons = weapons;
        }

        // --- drawing -------------------------------------------------------------------------------

        private void BuildBatches()
        {
            _ants = BugBatch(ModelLibrary.Ant, new Color(0.45f, 0.08f, 0.05f));
            _beetles = BugBatch(ModelLibrary.Beetle, new Color(0.35f, 0.15f, 0.06f));
            _bullets = new InstancedBatch(Meshes.Sphere, Looks.Glow(new Color(1f, 0.75f, 0.3f), 4f), castShadows: false);
            // Amber sugar crystals: the model's own baked colours and warm inner glow.
            var gem = ModelLibrary.LoadMesh(ModelLibrary.Gem);
            _gems = gem != null ? new InstancedBatch(gem.Mesh, gem.Materials, gem.Fit, castShadows: false)
                : new InstancedBatch(Meshes.Cube, Looks.Glow(new Color(1f, 0.6f, 0.2f), 1.2f), castShadows: false);
            _orbs = new InstancedBatch(Meshes.Sphere, Looks.Glow(new Color(0.55f, 0.85f, 1f), 6f), castShadows: false);
            _shells = new InstancedBatch(Meshes.Sphere, Looks.Plastic(new Color(0.45f, 0.3f, 0.12f), 0.6f));
            _blasts = new InstancedBatch(Meshes.Sphere, Looks.Additive(Color.white), castShadows: false, data0: "_Color");
            var puddle = new Material(Shader.Find("BulletHeaven/Puddle")) { enableInstancing = true };
            _puddles = new InstancedBatch(Meshes.Plane, puddle, castShadows: false, data0: "_Color", data1: "_Params");
            var mist = new Material(Shader.Find("BulletHeaven/Mist")) { enableInstancing = true };
            _puffs = new InstancedBatch(Meshes.Sphere, mist, castShadows: false, data0: "_Color");
        }

        /// <summary>The bug model through the walk-cycle shader, or a coloured blob if it isn't generated.</summary>
        private static InstancedBatch BugBatch(ModelSpec spec, Color fallback)
        {
            var model = ModelLibrary.LoadMesh(spec);
            if (model == null)
                return new InstancedBatch(Meshes.Sphere, Looks.Plastic(fallback, 0.35f));
            foreach (var material in model.Materials)
            {
                material.EnableKeyword("_BUGWALK");
                material.SetFloat("_BugWalk", 1f);
                material.SetFloat("_Matte", 0.6f); // waxy chitin, not plastic
            }
            return new InstancedBatch(model.Mesh, model.Materials, model.Fit, data0: "_Anim");
        }

        private void DrawBugs()
        {
            _ants.Begin();
            _beetles.Begin();
            _world.Query(in EnemyQuery, (ref Enemy enemy, ref Position p, ref Velocity v, ref Radius r, ref Elevation e) =>
            {
                var heading = v.Value.LengthSquared() > 0.01f ? v.Value.Normalized() : BulletHeaven.Numerics.Vector2.Right;
                var batch = enemy.Kind == EnemyKind.Beetle ? _beetles : _ants;
                float radius = r.Value;
                var at = Conv.World(p.Value, e.Height);

                // Climbing: nose up the wall, body held off its face. Airborne: nose follows the fall.
                float pitch = 0f;
                if (e.Climbing)
                {
                    pitch = 90f;
                    at -= Conv.World(heading, 0f) * radius * 0.8f;
                }
                else if (!e.Grounded)
                {
                    pitch = Mathf.Clamp(e.VerticalSpeed * 0.06f, -0.9f, 0.9f) * Mathf.Rad2Deg;
                }
                var rotation = Quaternion.Euler(0f, Conv.Heading(heading), 0f) * Quaternion.Euler(0f, 0f, pitch);
                float stride = Mathf.Clamp01(v.Value.Length() / 4f);
                batch.Add(at, rotation, Vector3.one * radius * 2f, new Vector4(enemy.WalkPhase, stride, enemy.HitFlash > 0f ? 1f : 0f, 0f));
            });
            _ants.Draw();
            _beetles.Draw();
        }

        private void DrawProjectiles(float dt)
        {
            var terrain = _state.Terrain;
            float time = _state.Time;

            _bullets.Begin();
            _world.Query(in BulletQuery, (ref Bullet b, ref Position p, ref Velocity v) =>
                _bullets.Add(Conv.World(p.Value, b.Height), Quaternion.Euler(0f, Conv.Heading(v.Value), 0f), new Vector3(0.9f, 0.12f, 0.12f)));
            _bullets.Draw();

            _gems.Begin();
            _world.Query(in GemQuery, (ref Position p) =>
                _gems.Add(Conv.World(p.Value, terrain.GroundHeight(p.Value) + 0.35f), Quaternion.Euler(0f, (time * 3f + p.Value.X) * Mathf.Rad2Deg, 0f), Vector3.one * 0.3f));
            _gems.Draw();

            _orbPositions.Clear();
            _orbs.Begin();
            _world.Query(in OrbQuery, (ref LightningOrb orb, ref Position p) =>
            {
                var at = Conv.World(p.Value, orb.Height + Mathf.Sin(time * 4f + p.Value.X) * 0.15f);
                float size = 0.64f + Mathf.Sin(time * 23f + p.Value.Y * 3f) * 0.1f;
                _orbs.Add(at, Quaternion.identity, Vector3.one * size);
                _orbPositions.Add(at);
            });
            _orbs.Draw();
            _lightning.Update(_state.Arcs, _orbPositions, _camera.transform.position, time, dt);

            _shells.Begin();
            _world.Query(in ShellQuery, (ref Shell s, ref Position p) =>
                _shells.Add(Conv.World(p.Value, s.Height), Quaternion.Euler(0f, 0f, s.Flight * 12f * Mathf.Rad2Deg), new Vector3(0.64f, 0.48f, 0.48f)));
            _shells.Draw();

            _blasts.Begin();
            _world.Query(in BlastQuery, (ref Blast b, ref Position p) =>
            {
                float t = b.Age / ShellSystem.BlastDuration;
                float size = b.Radius * (0.4f + 0.8f * t) * 2f;
                _blasts.Add(Conv.World(p.Value, b.Height + 0.3f), Quaternion.identity, new Vector3(size, size * 0.6f, size),
                    new Vector4(1f, 0.6f - 0.3f * t, 0.2f, 0.8f * (1f - t)));
            });
            _blasts.Draw();

            // Bug spray: pale, slightly green mist that swells and thins out.
            _puffs.Begin();
            _world.Query(in PuffQuery, (ref SprayPuff puff, ref Position p) =>
            {
                float t = puff.Age / puff.Lifetime;
                float alpha = 0.35f * Mathf.Min(1f, t * 6f) * (1f - t);
                _puffs.Add(Conv.World(p.Value, puff.Height), Quaternion.identity, Vector3.one * puff.Radius * 2f,
                    new Vector4(0.85f, 0.95f, 0.8f, alpha));
            });
            _puffs.Draw();
        }

        private void DrawWorldItems()
        {
            _puddles.Begin();
            _world.Query(in PuddleQuery, (ref Puddle puddle, ref Position p) =>
            {
                float fade = puddle.Lifetime < 0f ? 1f : Mathf.Clamp01((puddle.Lifetime - puddle.Age) / 1.5f);
                float grow = Mathf.Clamp(puddle.Age * 4f + (puddle.Lifetime < 0f ? 1f : 0f), 0.3f, 1f); // fresh splats spread out
                float thick = Mathf.Clamp01(1f - (puddle.Slow - 0.3f) / 0.4f);
                float seed = puddle.Seed - Mathf.Floor(puddle.Seed);
                var at = Conv.World(p.Value, puddle.Height + 0.03f + seed * 0.02f); // overlapping spills don't z-fight
                var color = Liquids.Color(puddle.Kind).ToUnity().linear;
                _puddles.Add(at, Quaternion.Euler(0f, seed * 360f, 0f), new Vector3(puddle.Radius * 2f, 1f, puddle.Radius * 2f) * grow,
                    new Vector4(color.r, color.g, color.b, 1f), new Vector4(seed, fade, thick, 0f));
            });
            _puddles.Draw();

            // Items bob and turn until picked up.
            foreach (var pair in _pickupViews)
            {
                if (!_world.IsAlive(pair.Key))
                {
                    pair.Value.gameObject.SetActive(false);
                    continue;
                }
                var pickup = _world.Get<Pickup>(pair.Key);
                var at = _world.Get<Position>(pair.Key).Value;
                pair.Value.localPosition = Conv.World(at, pickup.Height + 0.3f + Mathf.Sin(_state.Time * 2f) * 0.2f);
                pair.Value.localRotation = Quaternion.Euler(0f, _state.Time * Mathf.Rad2Deg, 0f);
            }
        }

        // --- scene setup ---------------------------------------------------------------------------

        /// <summary>
        /// Narrow field of view plus near/far depth of field = the tilt-shift miniature look; ACES tone
        /// mapping, bloom and a little extra saturation like the Godot version.
        /// </summary>
        private void BuildCameraAndLight()
        {
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.fieldOfView = 35f;
            _camera.farClipPlane = 400f;
            _camera.nearClipPlane = 0.3f;
            _camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.tag = "MainCamera";
            var cameraData = _camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(transform, false);
            _sun = sunObject.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            RenderSettings.sun = _sun;

            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
                _sky = new Material(skyShader);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.4f);
            bloom.threshold.Override(1f);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(15f);
            color.contrast.Override(5f);
            _depthOfField = profile.Add<DepthOfField>(true);
            _depthOfField.mode.Override(DepthOfFieldMode.Bokeh);
            _depthOfField.focusDistance.Override(CameraOffset.magnitude);
            _depthOfField.focalLength.Override(70f);
            _depthOfField.aperture.Override(5.6f);

            var volumeObject = new GameObject("Post-processing");
            volumeObject.transform.SetParent(transform, false);
            _volume = volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.sharedProfile = profile;
        }
    }
}
