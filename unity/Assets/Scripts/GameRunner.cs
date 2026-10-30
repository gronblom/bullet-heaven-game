using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Levels;
using BulletHeaven.Systems;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CoreModules = BulletHeaven.Systems.Modules;
using CoreTerrain = BulletHeaven.Ecs.Terrain;

namespace BulletHeaven.Unity
{
    /// <summary>
    /// The game: builds the level and the ECS world (gameplay lives in the shared core), fills in the
    /// controls, runs the systems in order and draws the result: instanced bugs, the runner, the level,
    /// and the arcade effects layer (<see cref="Effects"/>). Menus, the HUD and the module screen are in
    /// <see cref="GameUi"/>. It creates itself in whatever scene is open: just press Play.
    /// </summary>
    public sealed class GameRunner : MonoBehaviour
    {
        /// <summary>Level picked with F1-F3 or reached through a secret exit.</summary>
        private static int _selectedLevel;
        /// <summary>Modules carried through a secret exit into the next level: each weapon's slots, then the inventory.</summary>
        private static (Module[][] Slots, List<Module> Inventory)? _carried;

        private static readonly Vector3 CameraOffset = new Vector3(0f, 32f, -18f);
        private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Velocity, Radius, Elevation>();
        private static readonly QueryDescription BulletQuery = new QueryDescription().WithAll<Bullet, Position, Velocity>();
        private static readonly QueryDescription GemQuery = new QueryDescription().WithAll<XpGem, Position>();
        private static readonly QueryDescription OrbQuery = new QueryDescription().WithAll<LightningOrb, Position>();
        private static readonly QueryDescription VoidQuery = new QueryDescription().WithAll<VoidOrb, Position>();
        private static readonly QueryDescription RingQuery = new QueryDescription().WithAll<PulseRing, Position>();
        private static readonly QueryDescription BlastQuery = new QueryDescription().WithAll<Blast, Position>();
        private static readonly QueryDescription PuddleQuery = new QueryDescription().WithAll<Puddle, Position>();
        private static readonly QueryDescription PickupQuery = new QueryDescription().WithAll<Pickup, Position>();
        private static readonly QueryDescription DropQuery = new QueryDescription().WithAll<ModuleDrop, Position>();
        private static readonly QueryDescription TurretQuery = new QueryDescription().WithAll<Turret, Position>();

        private World _world;
        private GameState _state;
        private Level _level;
        private GameSystem[] _systems;
        private Transform _root;
        private RunnerView _runner;
        private OcclusionFader _fader;
        private Camera _camera;
        private Light _sun;
        private DepthOfField _depthOfField;
        private Material _sky;
        private Vector3 _cameraTarget;
        private GameUi _ui;
        private Effects _effects;
        private InstancedBatch _ants, _beetles, _queens, _gems, _voidCores, _puddles, _chips, _plates, _turrets;
        private LightningView _lightning;
        private readonly List<Vector3> _orbPositions = new List<Vector3>();
        private readonly Dictionary<Entity, Transform> _parachutes = new Dictionary<Entity, Transform>();
        private bool _choosing; // a level-up menu is open (pause can't close it)

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
            _effects = new Effects();
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
                new PlayerTag(), new Position(start), new Velocity(), new Health(100f), new Armor { Current = 50f, Max = 50f },
                new Elevation { Grounded = true }, new Radius(0.35f),
                new Runner { Facing = -Mathf.PI / 2f }, RunnerStats.Default);
            _runner = RunnerView.Create(_root);

            BuildBatches();
            _lightning = new LightningView(_root);
            _parachutes.Clear();
            _world.Query(in PickupQuery, (Entity entity, ref Pickup pickup, ref Position at) =>
            {
                if (pickup.Kind != PickupKind.Parachute)
                    return;
                var view = ModelLibrary.LoadProp("parachute");
                if (view == null)
                {
                    view = LevelView.MeshObject("parachute", Meshes.Sphere, Looks.Plastic(new Color(0.9f, 0.35f, 0.05f), 0.7f), _root, true);
                    view.transform.localScale = new Vector3(1.8f, 0.9f, 1.8f);
                }
                view.transform.SetParent(_root, false);
                _parachutes[entity] = view.transform;
            });

            // Order matters: input -> physics (steering, then pulls and slows) -> movement and collisions
            // -> spatial index -> weapons and hits -> deaths and pickups -> spawning.
            _systems = new GameSystem[]
            {
                new PlayerInputSystem(_world, _state),
                new RunnerPhysicsSystem(_world, _state),
                new CarPhysicsSystem(_world, _state),
                new EnemySteeringSystem(_world, _state),
                new BossSystem(_world, _state),
                new LiquidSystem(_world, _state),
                new VoidSystem(_world, _state),
                new PatternMotionSystem(_world, _state),
                new MovementSystem(_world, _state),
                new CollisionSystem(_world, _state),
                new VerticalPhysicsSystem(_world, _state),
                new ItemSystem(_world, _state),
                new EnemyGridSystem(_world, _state),
                new PlayerContactSystem(_world, _state),
                new WeaponSystem(_world, _state),
                new BulletSystem(_world, _state),
                new LightningOrbSystem(_world, _state),
                new PulseRingSystem(_world, _state),
                new TurretSystem(_world, _state),
                new BlastSystem(_world, _state),
                new EnemyDeathSystem(_world, _state),
                new PickupSystem(_world, _state),
                new EnemySpawnSystem(_world, _state),
            };

            if (_carried is { } carried)
            {
                for (int w = 0; w < _state.Weapons.Count && w < carried.Slots.Length; w++)
                {
                    carried.Slots[w].CopyTo(_state.Weapons[w].Slots, 0);
                    _state.Weapons[w].Recompute();
                }
                _state.Inventory.AddRange(carried.Inventory);
                _carried = null;
            }

            _cameraTarget = Conv.World(start, 0f);
            _choosing = false;
            _ui.Close();
            _state.ShowMessage($"{_level.Name}\nWASD / stick to run, Space / A to jump, Tab: modules\nEsc / Start: pause   F1-F3: switch level", 5f);
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

        /// <summary>Level-up: pause and offer three modules. A weapon module goes to the inventory and the
        /// module screen opens on it, so you can slot it right away.</summary>
        private void ChooseModule()
        {
            _state.PendingLevelUps--;
            var offers = CoreModules.Roll(_state, 3);
            _choosing = true;
            _ui.ShowCards($"LEVEL {_state.Level}", "Pick one: 1-3 / click / stick + A", Cards.Modules(offers), i =>
            {
                _choosing = false;
                var module = offers[i];
                if (module.Op == ModuleOp.Player)
                {
                    CoreModules.Take(_world, _state, module, autoFit: false);
                    _state.ShowMessage(module.Name, 1.5f);
                }
                else if (CoreModules.Take(_world, _state, module, autoFit: false))
                {
                    _ui.Modules.Open(highlight: _state.Inventory.Count - 1);
                }
                else if (CoreModules.AutoFit(_state, module))
                {
                    _state.ShowMessage($"{module.Name}: slotted (inventory full)", 2f);
                }
                else
                {
                    _state.ShowMessage("Inventory full and no free slot: make room with Tab", 2.5f);
                }
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

        /// <summary>Through the secret exit: remember it's unlocked and carry on with the same modules.</summary>
        private void TakeExit()
        {
            PlayerPrefs.SetInt("unlocked." + Level.All[_level.ExitTo].Name, 1);
            PlayerPrefs.Save();
            _carried = (_state.Weapons.ConvertAll(w => (Module[])w.Slots.Clone()).ToArray(), new List<Module>(_state.Inventory));
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
            bool modules = Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.JoystickButton6);
            bool simulate = false;
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
            else if (modules && !_ui.CardsOpen)
            {
                if (_ui.Modules.IsOpen)
                    _ui.Modules.Close();
                else
                    _ui.Modules.Open();
            }
            else if (!_ui.IsOpen && _state.PendingLevelUps > 0)
            {
                ChooseModule();
            }
            else if (!_ui.IsOpen && _state.ReachedExit)
            {
                TakeExit();
                return;
            }
            else if (!_ui.IsOpen)
            {
                simulate = true;
            }

            if (simulate)
            {
                DebugKeys();
                ReadInput();
                _state.BeginFrame();
                _state.Time += dt;
                foreach (var system in _systems)
                    system.Update(dt);
                _state.FlushDestroyed(_world);
            }

            if (_world != null)
                SyncVisuals(simulate ? dt : 0f);
        }

        /// <summary>Legacy input manager to the engine-independent controls (screen up = -Y on the ground plane).</summary>
        private void ReadInput()
        {
            var move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 1f)
                move.Normalize();
            _state.Input = new PlayerInput
            {
                Move = new BulletHeaven.Numerics.Vector2(move.x, -move.y),
                JumpPressed = Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.JoystickButton0),
                JumpHeld = Input.GetButton("Jump") || Input.GetKey(KeyCode.JoystickButton0),
            };
        }

        /// <summary>Shift + 1-5: a random module of each kind into the inventory (development).</summary>
        private void DebugKeys()
        {
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                return;
            for (int i = 0; i < 4; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + i))
                    continue;
                var op = (ModuleOp)i;
                var choices = System.Array.FindAll(CoreModules.All, m => m.Op == op);
                var module = choices[Random.Range(0, choices.Length)];
                CoreModules.Take(_world, _state, module, autoFit: false);
                _state.ShowMessage($"Debug: {module.Name}", 1.2f);
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
            float minigunSpin = _state.Weapon(WeaponKind.Minigun)?.Spin ?? 0f;

            _runner.ShowWeapons(_state.Weapons);
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

            _effects.Begin(_camera, _state.Time);
            int bugs = DrawBugs();
            DrawProjectiles(dt);
            DrawWorldItems();
            var facing = new BulletHeaven.Numerics.Vector2(Mathf.Cos(runner.Facing), Mathf.Sin(runner.Facing));
            var gun = Conv.World(position.Value + facing * 0.5f, elevation.Height + 0.75f);
            if (dt > 0f)
                _effects.Play(_state.Events, gun);
            _effects.End(dt);

            _ui.EnemyCount = bugs;
            _ui.Health = health;
            _ui.Armor = _world.Get<Armor>(player);
        }

        // --- drawing -------------------------------------------------------------------------------

        private void BuildBatches()
        {
            _ants = BugBatch(ModelLibrary.Ant, new Color(0.45f, 0.08f, 0.05f));
            _beetles = BugBatch(ModelLibrary.Beetle, new Color(0.35f, 0.15f, 0.06f));
            _queens = BugBatch(ModelLibrary.Ant, new Color(0.45f, 0.08f, 0.05f));
            // Amber sugar crystals: the model's own baked colours and warm inner glow.
            var gem = ModelLibrary.LoadMesh(ModelLibrary.Gem);
            _gems = gem != null ? new InstancedBatch(gem.Mesh, gem.Materials, gem.Fit, castShadows: false)
                : new InstancedBatch(Meshes.Cube, Looks.Glow(new Color(1f, 0.6f, 0.2f), 1.2f), castShadows: false);
            // Void orbs: a pitch-black core (the glow halo is an effect).
            _voidCores = new InstancedBatch(Meshes.Sphere, Looks.Plastic(new Color(0.02f, 0.0f, 0.04f), 0.15f), castShadows: false);
            var puddle = new Material(Shader.Find("BulletHeaven/Puddle")) { enableInstancing = true };
            _puddles = new InstancedBatch(Meshes.Plane, puddle, castShadows: false, data0: "_Color", data1: "_Params");
            _chips = new InstancedBatch(Meshes.Cube, Looks.Plastic(new Color(0.12f, 0.1f, 0.1f), 0.25f, 0.6f));
            _plates = new InstancedBatch(Meshes.Cube, Looks.Plastic(new Color(0.5f, 0.55f, 0.62f), 0.3f, 0.9f));
            var mortar = ModelLibrary.LoadMesh(new ModelSpec("mortar", 2.2f, 0f));
            _turrets = mortar != null ? new InstancedBatch(mortar.Mesh, mortar.Materials, mortar.Fit)
                : new InstancedBatch(Meshes.Cylinder(), Looks.Plastic(new Color(0.3f, 0.35f, 0.18f), 0.5f, 0.4f));
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

        private int DrawBugs()
        {
            _ants.Begin();
            _beetles.Begin();
            _queens.Begin();
            _world.Query(in EnemyQuery, (ref Enemy enemy, ref Position p, ref Velocity v, ref Radius r, ref Elevation e) =>
            {
                var heading = v.Value.LengthSquared() > 0.01f ? v.Value.Normalized() : BulletHeaven.Numerics.Vector2.Right;
                var batch = enemy.Kind switch { EnemyKind.Beetle => _beetles, EnemyKind.Queen => _queens, _ => _ants };
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
            _queens.Draw();
            return _ants.Count + _beetles.Count + _queens.Count;
        }

        private void DrawProjectiles(float dt)
        {
            var terrain = _state.Terrain;
            float time = _state.Time;
            var fx = _effects;

            // Minigun rounds: glowing streaks.
            var bulletColor = Effects.WeaponColor(WeaponKind.Minigun);
            _world.Query(in BulletQuery, (ref Bullet b, ref Position p, ref Velocity v) =>
                fx.Bullet(Conv.World(p.Value, b.Height), new Vector3(v.Value.X, 0f, -v.Value.Y), bulletColor));

            foreach (var beam in _state.Lasers)
                fx.Laser(beam.From.ToUnity(), beam.To.ToUnity(), beam.Width);

            _gems.Begin();
            _world.Query(in GemQuery, (ref Position p) =>
                _gems.Add(Conv.World(p.Value, terrain.GroundHeight(p.Value) + 0.35f), Quaternion.Euler(0f, (time * 3f + p.Value.X) * Mathf.Rad2Deg, 0f), Vector3.one * 0.3f));
            _gems.Draw();

            // Arcane wand orbs: blue-white balls of light (with real lights, see LightningView).
            _orbPositions.Clear();
            var orbs = _orbPositions;
            _world.Query(in OrbQuery, (ref LightningOrb orb, ref Position p) =>
            {
                var at = Conv.World(p.Value, orb.Height + Mathf.Sin(time * 4f + p.Value.X) * 0.15f);
                float size = 0.9f + Mathf.Sin(time * 23f + p.Value.Y * 3f) * 0.15f;
                fx.Dot(at, size * 2.2f, new Color(0.35f, 0.65f, 1f, 0.7f));
                fx.Dot(at, size, new Color(0.9f, 0.97f, 1f, 1f));
                orbs.Add(at);
            });
            _lightning.Update(_state.Arcs, _orbPositions, _camera.transform.position, time, dt);

            // Void orbs: black core, violet halo, motes sucked in.
            _voidCores.Begin();
            var cores = _voidCores;
            _world.Query(in VoidQuery, (ref VoidOrb orb, ref Position p) =>
            {
                var at = Conv.World(p.Value, orb.Height);
                float grow = Mathf.Clamp01(orb.Age * 4f);
                cores.Add(at, Quaternion.identity, Vector3.one * orb.CoreRadius * 0.9f * grow);
                fx.VoidOrb(at, orb.PullRadius, orb.CoreRadius * grow);
            });
            _voidCores.Draw();

            // TD Mortar rings: burning fronts.
            _world.Query(in RingQuery, (ref PulseRing ring, ref Position p) => fx.Ring(p.Value, ring));

            // Implosion flashes.
            _world.Query(in BlastQuery, (ref Blast b, ref Position p) =>
            {
                float t = b.Age / BlastSystem.Duration;
                fx.Dot(Conv.World(p.Value, b.Height + 0.6f), b.Radius * 2.2f * (0.5f + t), new Color(0.75f, 0.45f, 1f, 0.8f * (1f - t)));
            });
        }

        private void DrawWorldItems()
        {
            float time = _state.Time;
            var fx = _effects;

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

            // Dropped modules: spinning chips glowing in their kind's colour (blinking before they vanish).
            _chips.Begin();
            var chips = _chips;
            _world.Query(in DropQuery, (ref ModuleDrop drop, ref Position p) =>
            {
                if (drop.Age > ItemSystem.DropLifetime - 5f && Mathf.Repeat(time * 6f, 1f) < 0.4f)
                    return;
                var at = Conv.World(p.Value, drop.Height + 0.55f + Mathf.Sin(time * 3f + p.Value.X) * 0.12f);
                chips.Add(at, Quaternion.Euler(20f, time * 120f + p.Value.X * 40f, 0f), new Vector3(0.55f, 0.12f, 0.55f));
                var accent = Cards.Accent(CoreModules.Get(drop.Module).Op).ToUnity();
                fx.Dot(at, 1.6f, new Color(accent.r, accent.g, accent.b, 0.65f));
            });
            _chips.Draw();

            // Armour plates and turret kits lying around; the parachute keeps its own model.
            _plates.Begin();
            var plates = _plates;
            _world.Query(in PickupQuery, (ref Pickup pickup, ref Position p) =>
            {
                if (pickup.Kind == PickupKind.Parachute)
                    return;
                var at = Conv.World(p.Value, pickup.Height + 0.5f + Mathf.Sin(time * 2.5f) * 0.1f);
                var spin = Quaternion.Euler(0f, time * 90f, 0f);
                if (pickup.Kind == PickupKind.ArmorPlate)
                {
                    plates.Add(at, spin, new Vector3(0.7f, 0.18f, 0.5f));
                    fx.Dot(at, 1.5f, new Color(0.35f, 0.65f, 1f, 0.6f));
                }
                else
                {
                    plates.Add(at, spin, new Vector3(0.45f, 0.45f, 0.45f));
                    fx.Dot(at, 1.5f, new Color(1f, 0.45f, 0.12f, 0.6f));
                }
            });
            _plates.Draw();

            // Turrets: a big mortar on the spot, blinking in its last seconds, a glowing base ring.
            _turrets.Begin();
            var turrets = _turrets;
            _world.Query(in TurretQuery, (ref Turret turret, ref Position p) =>
            {
                if (turret.Lifetime < 4f && Mathf.Repeat(time * 5f, 1f) < 0.3f)
                    return;
                var at = Conv.World(p.Value, turret.Height);
                turrets.Add(at, Quaternion.Euler(0f, time * 30f, 0f), Vector3.one);
                fx.Dot(at + Vector3.up * 0.2f, 3.2f, new Color(1f, 0.45f, 0.12f, 0.35f));
            });
            _turrets.Draw();

            foreach (var pair in _parachutes)
            {
                if (!_world.IsAlive(pair.Key))
                {
                    pair.Value.gameObject.SetActive(false);
                    continue;
                }
                var pickup = _world.Get<Pickup>(pair.Key);
                var at = _world.Get<Position>(pair.Key).Value;
                pair.Value.localPosition = Conv.World(at, pickup.Height + 0.3f + Mathf.Sin(time * 2f) * 0.2f);
                pair.Value.localRotation = Quaternion.Euler(0f, time * Mathf.Rad2Deg, 0f);
            }
        }

        // --- scene setup ---------------------------------------------------------------------------

        /// <summary>
        /// Narrow field of view plus near/far depth of field = the tilt-shift miniature look; ACES tone
        /// mapping, strong bloom for the arcade effects, and a little extra saturation.
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
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(1f);
            bloom.scatter.Override(0.75f);
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
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }
    }
}
