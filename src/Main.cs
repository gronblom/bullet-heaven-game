using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Levels;
using BulletHeaven.Rendering;
using BulletHeaven.Systems;
using Godot;

namespace BulletHeaven;

/// <summary>
/// Entry point. Godot owns the window, input and rendering; all gameplay state lives in the Arch
/// ECS world and is advanced by the systems below. Each frame: run systems, then copy ECS state
/// into the visuals (player node, MultiMesh batches, effects, camera, HUD).
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Tick in the Inspector (select the Main node) to play as the toy car instead of the runner.</summary>
    [Export] public bool DriveCar { get; set; }

    /// <summary>Level to start in (0 = Kitchen); F1-F3 switch levels while playing.</summary>
    [Export] public int StartLevel { get; set; }

    /// <summary>The level picked with F1-F3 or reached by a secret exit; survives the scene reload.</summary>
    private static int? _selectedLevel;
    /// <summary>Weapons carried through a secret exit into the next level (skips the loadout menu once).</summary>
    private static WeaponKind[] _carriedLoadout;
    private const string ProgressFile = "user://progress.cfg";

    private static readonly Vector3 CameraOffset = new(0f, 32f, 18f);
    private static readonly Color AntColor = new(0.45f, 0.08f, 0.05f);
    private static readonly Color BeetleColor = new(0.35f, 0.15f, 0.06f);

    private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Velocity, Radius, Elevation>();
    private static readonly QueryDescription BulletQuery = new QueryDescription().WithAll<Bullet, Position, Velocity>();
    private static readonly QueryDescription GemQuery = new QueryDescription().WithAll<XpGem, Position>();
    private static readonly QueryDescription OrbQuery = new QueryDescription().WithAll<LightningOrb, Position>();
    private static readonly QueryDescription ShellQuery = new QueryDescription().WithAll<Shell, Position>();
    private static readonly QueryDescription BlastQuery = new QueryDescription().WithAll<Blast, Position>();
    private static readonly QueryDescription PuddleQuery = new QueryDescription().WithAll<Puddle, Position>();
    private static readonly QueryDescription PickupQuery = new QueryDescription().WithAll<Pickup, Position>();

    private World _world;
    private GameState _state;
    private Level _level;
    private GameSystem[] _systems;

    private CarView _carView;
    private RunnerView _runnerView;
    private OcclusionFader _fader;
    private Camera3D _camera;
    private Vector3 _cameraTarget;
    private Hud _hud;
    private Overlay _overlay;
    private bool _choosing; // a loadout or level-up menu is open (pause can't close it)
    private InstancedRenderer _puddleRenderer;
    private readonly Dictionary<Entity, Node3D> _pickupViews = new();
    private InstancedRenderer _antRenderer;
    private InstancedRenderer _beetleRenderer;
    private InstancedRenderer _bulletRenderer;
    private InstancedRenderer _gemRenderer;
    private InstancedRenderer _orbRenderer;
    private InstancedRenderer _shellRenderer;
    private InstancedRenderer _blastRenderer;
    private LightningRenderer _lightning;
    private readonly List<Vector3> _orbPositions = new();

    public override void _Ready()
    {
        GameInput.Register();

        _level = Level.All[Math.Clamp(_selectedLevel ?? StartLevel, 0, Level.All.Length - 1)];
        _world = World.Create();
        _state = new GameState(_level.Bounds);
        _fader = new OcclusionFader();

        var (environment, sun) = BuildEnvironment();
        var layout = _level.Build(_world);
        LevelView.Build(this, _level, layout, _fader);
        LevelView.Atmosphere(environment, sun, this, _level.Atmosphere, layout);
        _state.Terrain = Terrain.FromWorld(_world, _level.Bounds);
        _state.ExitArea = _level.Exit;
        _state.ExitHeight = _level.ExitHeight;
        if (_level.BossSpawn is { } bossSpawn)
        {
            _state.BossLevel = true;
            _state.BossSpawn = bossSpawn;
            _state.SpawnRateScale = 0.6f; // the queen brings her own ants
        }

        var start = _level.PlayerStart;
        const float facingUp = -Mathf.Pi / 2f; // up the screen
        _state.Player = _world.Create(
            new PlayerTag(),
            new Position(start),
            new Velocity(),
            new Health(100f),
            new Elevation { Grounded = true });

        if (DriveCar)
        {
            _world.Add(_state.Player, new Radius(0.6f), new Car { Heading = facingUp }, CarStats.Default);
            _carView = new CarView(new Color(0.9f, 0.15f, 0.1f));
            AddChild(_carView);
        }
        else
        {
            _world.Add(_state.Player, new Radius(0.35f), new Runner { Facing = facingUp }, RunnerStats.Default);
            _runnerView = new RunnerView();
            AddChild(_runnerView);
        }

        // One batch per enemy kind so each can use its own imported model; models walk via the shader.
        var bugShader = GD.Load<Shader>("res://shaders/bug_walk.gdshader");
        _antRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 },
            Materials.InstanceColored(0.35f), ModelLibrary.Ant, animationShader: bugShader);
        _beetleRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 },
            Materials.InstanceColored(0.35f), ModelLibrary.Beetle, animationShader: bugShader);
        _bulletRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 8, Rings = 4 },
            Materials.Glow(new Color(1f, 0.75f, 0.3f), 4f), castShadows: false);
        _gemRenderer = new InstancedRenderer(this, new BoxMesh(), Materials.Glow(new Color(0.3f, 1f, 0.5f), 1.5f),
            ModelLibrary.Gem, castShadows: false);
        _orbRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f },
            Materials.Glow(new Color(0.55f, 0.85f, 1f), 6f), castShadows: false);
        _shellRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 },
            Materials.Plastic(new Color(0.45f, 0.3f, 0.12f), 0.6f));
        _blastRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f }, new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
        }, castShadows: false);
        _lightning = new LightningRenderer(this);
        var puddleMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/puddle.gdshader") };
        _puddleRenderer = new InstancedRenderer(this, new PlaneMesh { Size = Vector2.One * 2f }, puddleMaterial, castShadows: false);
        foreach (var (entity, height, at) in Pickups())
        {
            var view = ModelLibrary.LoadProp("parachute")?.Node ?? new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.9f, Height = 0.9f, IsHemisphere = true },
                MaterialOverride = Materials.Plastic(new Color(0.9f, 0.35f, 0.05f), 0.7f),
            };
            view.Position = new Vector3(at.X, height, at.Y);
            AddChild(view);
            _pickupViews[entity] = view;
        }

        _hud = new Hud();
        AddChild(_hud);
        _overlay = new Overlay();
        AddChild(_overlay);

        // Order matters: input -> physics -> collisions -> spatial index -> combat -> cleanup.
        _systems = new GameSystem[]
        {
            new PlayerInputSystem(_world, _state),
            new RunnerPhysicsSystem(_world, _state),
            new CarPhysicsSystem(_world, _state),
            new EnemySteeringSystem(_world, _state),
            new BossSystem(_world, _state),
            new LiquidSystem(_world, _state),
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

        _cameraTarget = new Vector3(start.X, 0f, start.Y);
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

    private void StartRun() =>
        _state.ShowMessage($"{_level.Name}\n" + (DriveCar
            ? "WASD / arrows to drive, Shift to drift, Space to jump"
            : "WASD / arrows / stick to run, Space / A to jump") + "\nEsc / Start: pause   F1-F3: switch level", 5f);

    /// <summary>Run start: pick two of the three weapons, one card at a time.</summary>
    private void ChooseLoadout()
    {
        _choosing = true;
        var kinds = new List<WeaponKind> { WeaponKind.Minigun, WeaponKind.LightningWand, WeaponKind.SeedMortar };
        void Offer(string title)
        {
            var cards = kinds.ConvertAll(k => (WeaponStats.Name(k), WeaponStats.Blurb(k)));
            _overlay.ShowCards(title, "1-3 / click / d-pad + A", cards, i =>
            {
                Arsenal.Grant(_world, kinds[i]);
                kinds.RemoveAt(i);
                if (Arsenal.Carried(_world).Count < 2)
                {
                    Offer("Pick your second weapon");
                    return;
                }
                _choosing = false;
                StartRun();
            });
        }
        Offer($"{_level.Name}\nPick your first weapon");
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
        var cards = offers.ConvertAll(m => (m.Weapon is { } kind ? $"{m.Name}\n({WeaponStats.Name(kind)})" : m.Name, m.Description));
        _overlay.ShowCards($"LEVEL {_state.Level}", "Pick one: 1-3 / click / d-pad + A", cards, i =>
        {
            Modifiers.Pick(_world, _state, offers[i]);
            _choosing = false;
        });
    }

    private void TogglePause()
    {
        if (_overlay.IsOpen)
        {
            _overlay.Close();
            return;
        }
        var cards = new List<(string, string)> { ("Resume", "Back to the bugs"), ("Restart", "Start this level over"), ("Kitchen", "Back to the first level") };
        _overlay.ShowCards("PAUSED", "Esc / Start to resume", cards, i =>
        {
            if (i == 1)
                Restart(_selectedLevel ?? StartLevel);
            else if (i == 2)
                Restart(0);
        });
    }

    private void Restart(int level)
    {
        _selectedLevel = level;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Through the secret exit: remember it's unlocked and carry on in the next level with the same weapons.</summary>
    private void TakeExit()
    {
        var progress = new ConfigFile();
        progress.Load(ProgressFile);
        progress.SetValue("unlocked", Level.All[_level.ExitTo].Name, true);
        progress.Save(ProgressFile);
        _carriedLoadout = Arsenal.Carried(_world).ConvertAll(w => w.Kind).ToArray();
        Restart(_level.ExitTo);
    }

    private List<(Entity Entity, float Height, Vector2 At)> Pickups()
    {
        var list = new List<(Entity, float, Vector2)>();
        _world.Query(in PickupQuery, (Entity e, ref Pickup p, ref Position at) => list.Add((e, p.Height, at.Value.ToGodot())));
        return list;
    }

    public override void _Process(double delta)
    {
        float dt = Math.Min((float)delta, 1f / 20f); // avoid huge steps after hitches

        for (int i = 0; i < GameInput.Levels.Length && i < Level.All.Length; i++)
        {
            if (Input.IsActionJustPressed(GameInput.Levels[i]))
            {
                Restart(i);
                return;
            }
        }

        if (_state.GameOver)
        {
            if (Input.IsActionJustPressed(GameInput.Restart) || Input.IsActionJustPressed(GameInput.Pause))
            {
                Restart(_selectedLevel ?? StartLevel);
                return;
            }
        }
        else if (Input.IsActionJustPressed(GameInput.Pause) && !_choosing)
        {
            TogglePause();
        }
        else if (!_overlay.IsOpen && _state.PendingLevelUps > 0)
        {
            ChooseModifier();
        }
        else if (!_overlay.IsOpen && _state.ReachedExit)
        {
            TakeExit();
            return;
        }
        else if (!_overlay.IsOpen)
        {
            DebugWeaponKeys();
            ReadInput();
            _state.Time += dt;
            foreach (var system in _systems)
                system.Update(dt);
            _state.FlushDestroyed(_world);
        }

        SyncVisuals(dt);
    }

    /// <summary>Godot's input map into the engine-independent controls the gameplay systems read.</summary>
    private void ReadInput()
    {
        // Screen up is -Y on the ground plane, so the vector maps straight onto it.
        _state.Input = new PlayerInput
        {
            Move = Input.GetVector(GameInput.SteerLeft, GameInput.SteerRight, GameInput.MoveUp, GameInput.MoveDown).ToCore(),
            JumpPressed = Input.IsActionJustPressed(GameInput.RunnerJump),
            JumpHeld = Input.IsActionPressed(GameInput.RunnerJump),
            Throttle = Input.GetActionStrength(GameInput.Accelerate) - Input.GetActionStrength(GameInput.Brake),
            Steer = Input.GetAxis(GameInput.SteerLeft, GameInput.SteerRight),
            Handbrake = Input.IsActionPressed(GameInput.Handbrake),
            CarJump = Input.IsActionJustPressed(GameInput.Jump),
        };
    }

    public override void _ExitTree()
    {
        if (_world != null)
            World.Destroy(_world);
        _world = null;
    }

    /// <summary>Shift + 1-3 adds a weapon, for trying them out.</summary>
    private void DebugWeaponKeys()
    {
        for (int i = 0; i < GameInput.DebugWeapons.Length; i++)
        {
            if (Input.IsActionJustPressed(GameInput.DebugWeapons[i]))
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
        var weapons = Arsenal.Carried(_world);
        float minigunSpin = weapons.Find(w => w.Kind == WeaponKind.Minigun).Spin; // default (0) if not carried

        if (DriveCar)
        {
            _carView.Sync(position, _world.Get<Car>(player), elevation, health, dt);
        }
        else
        {
            _runnerView.ShowWeapons(weapons.Exists(w => w.Kind == WeaponKind.Minigun), weapons.Exists(w => w.Kind == WeaponKind.LightningWand));
            _runnerView.Sync(position, velocity, _world.Get<Runner>(player), elevation, health, minigunSpin, dt);
            _runnerView.SyncParachute(_world.Get<Runner>(player).Gliding, dt);
        }

        // Camera never rotates; it trails the player with some look-ahead and follows them up and
        // down levels (smoothly, so a jump doesn't bounce the view).
        var lookAhead = new Vector3(velocity.Value.X, 0f, velocity.Value.Y) * (DriveCar ? 0.45f : 0.3f);
        var desired = new Vector3(position.Value.X, elevation.Height, position.Value.Y) + lookAhead;
        _cameraTarget.X = Mathf.Lerp(_cameraTarget.X, desired.X, 1f - MathF.Exp(-4f * dt));
        _cameraTarget.Z = Mathf.Lerp(_cameraTarget.Z, desired.Z, 1f - MathF.Exp(-4f * dt));
        _cameraTarget.Y = Mathf.Lerp(_cameraTarget.Y, desired.Y, 1f - MathF.Exp(-2.5f * dt));
        _camera.Position = _cameraTarget + CameraOffset;

        var playerPoint = new Vector3(position.Value.X, elevation.Height + 0.5f, position.Value.Y);
        _fader.Update(_camera.Position, playerPoint, dt);

        int enemyCount = DrawBugs();
        DrawProjectiles(dt);
        DrawWorldItems();

        _hud.Sync(_state, _level.Name, health, enemyCount, velocity.Value.Length(), weapons, dt);
    }

    private void DrawWorldItems()
    {
        _puddleRenderer.Begin();
        _world.Query(in PuddleQuery, (ref Puddle puddle, ref Position p) =>
        {
            float fade = puddle.Lifetime < 0f ? 1f : Math.Clamp((puddle.Lifetime - puddle.Age) / 1.5f, 0f, 1f);
            float grow = Math.Clamp(puddle.Age * 4f + (puddle.Lifetime < 0f ? 1f : 0f), 0.3f, 1f); // fresh splats spread out
            float thick = 1f - (puddle.Slow - 0.3f) / 0.4f;
            float seed = puddle.Seed - MathF.Floor(puddle.Seed);
            var at = new Vector3(p.Value.X, puddle.Height + 0.03f + seed * 0.02f, p.Value.Y); // overlapping spills don't z-fight
            _puddleRenderer.Add(at, seed * Mathf.Tau, new Vector3(puddle.Radius, 1f, puddle.Radius) * grow,
                Liquids.Color(puddle.Kind).ToGodot(), custom: new Color(seed, fade, Math.Clamp(thick, 0f, 1f), 0f));
        });
        _puddleRenderer.End();

        // Items bob and turn until picked up.
        foreach (var (entity, view) in _pickupViews)
        {
            if (!_world.IsAlive(entity))
            {
                view.Visible = false;
                continue;
            }
            var pickup = _world.Get<Pickup>(entity);
            var at = _world.Get<Position>(entity).Value;
            view.Position = new Vector3(at.X, pickup.Height + 0.3f + MathF.Sin(_state.Time * 2f) * 0.2f, at.Y);
            view.Rotation = new Vector3(0f, _state.Time, 0f);
        }
    }

    private int DrawBugs()
    {
        int count = 0;
        _antRenderer.Begin();
        _beetleRenderer.Begin();
        _world.Query(in EnemyQuery, (ref Enemy enemy, ref Position p, ref Velocity v, ref Radius r, ref Elevation e) =>
        {
            var heading = v.Value.LengthSquared() > 0.01f ? v.Value.Normalized().ToGodot() : Vector2.Right;
            float yaw = -heading.Angle();
            var renderer = enemy.Kind == EnemyKind.Beetle ? _beetleRenderer : _antRenderer;
            float radius = r.Value;
            var at = new Vector3(p.Value.X, e.Height, p.Value.Y);

            // Climbing: nose up the wall, body held off its face. Airborne: nose follows the fall.
            float pitch = 0f;
            if (e.Climbing)
            {
                pitch = Mathf.Pi / 2f;
                at -= new Vector3(heading.X, 0f, heading.Y) * radius * 0.8f;
            }
            else if (!e.Grounded)
            {
                pitch = Math.Clamp(e.VerticalSpeed * 0.06f, -0.9f, 0.9f);
            }

            if (renderer.UsesModel)
            {
                // Models are fitted to unit length and stand on the ground; the shader walks their legs.
                float stride = Math.Clamp(v.Value.Length() / 4f, 0f, 1f);
                var custom = new Color(enemy.WalkPhase, stride, enemy.HitFlash > 0f ? 1f : 0f, 0f);
                renderer.Add(at, yaw, Vector3.One * radius * 2f, Colors.White, pitch, custom);
            }
            else
            {
                var color = enemy.HitFlash > 0f ? Colors.White : enemy.Kind == EnemyKind.Beetle ? BeetleColor : AntColor;
                float pop = 1f + enemy.HitFlash * 2f;
                renderer.Add(at + new Vector3(0f, radius * 0.6f, 0f), yaw, new Vector3(radius * 1.4f, radius * 0.6f, radius) * pop, color, pitch);
            }
            count++;
        });
        _antRenderer.End();
        _beetleRenderer.End();
        return count;
    }

    private void DrawProjectiles(float dt)
    {
        var terrain = _state.Terrain;
        float time = _state.Time;

        // Minigun tracers: stretched along their flight.
        _bulletRenderer.Begin();
        _world.Query(in BulletQuery, (ref Bullet b, ref Position p, ref Velocity v) =>
            _bulletRenderer.Add(new Vector3(p.Value.X, b.Height, p.Value.Y), -v.Value.Angle(), new Vector3(0.45f, 0.06f, 0.06f), Colors.White));
        _bulletRenderer.End();

        float spin = time * 3f;
        _gemRenderer.Begin();
        _world.Query(in GemQuery, (ref Position p) =>
        {
            var at = new Vector3(p.Value.X, terrain.GroundHeight(p.Value) + 0.35f, p.Value.Y);
            _gemRenderer.Add(at, spin + p.Value.X, new Vector3(0.3f, 0.3f, 0.3f), Colors.White);
        });
        _gemRenderer.End();

        // Ball lightning: a pulsing glow plus a real light each (see LightningRenderer).
        _orbPositions.Clear();
        _orbRenderer.Begin();
        _world.Query(in OrbQuery, (ref LightningOrb orb, ref Position p) =>
        {
            var at = new Vector3(p.Value.X, orb.Height + MathF.Sin(time * 4f + p.Value.X) * 0.15f, p.Value.Y);
            float size = 0.32f + MathF.Sin(time * 23f + p.Value.Y * 3f) * 0.05f;
            _orbRenderer.Add(at, time * 5f, Vector3.One * size, Colors.White);
            _orbPositions.Add(at);
        });
        _orbRenderer.End();
        _lightning.Update(_state.Arcs, _orbPositions, _camera.Position, time, dt);

        _shellRenderer.Begin();
        _world.Query(in ShellQuery, (ref Shell s, ref Position p) =>
            _shellRenderer.Add(new Vector3(p.Value.X, s.Height, p.Value.Y), s.Flight * 12f, new Vector3(0.32f, 0.24f, 0.24f), new Color(0.45f, 0.3f, 0.12f)));
        _shellRenderer.End();

        _blastRenderer.Begin();
        _world.Query(in BlastQuery, (ref Blast b, ref Position p) =>
        {
            float t = b.Age / ShellSystem.BlastDuration;
            float size = b.Radius * (0.4f + 0.8f * t);
            _blastRenderer.Add(new Vector3(p.Value.X, b.Height + 0.3f, p.Value.Y), 0f, new Vector3(size, size * 0.6f, size),
                new Color(1f, 0.6f - 0.3f * t, 0.2f, 0.8f * (1f - t)));
        });
        _blastRenderer.End();
    }

    /// <summary>Default daylight look; each level's Atmosphere adjusts it.</summary>
    private (Godot.Environment, DirectionalLight3D) BuildEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.45f, 0.58f, 0.78f),
                    SkyHorizonColor = new Color(0.8f, 0.72f, 0.6f),
                    GroundHorizonColor = new Color(0.8f, 0.72f, 0.6f),
                    GroundBottomColor = new Color(0.3f, 0.27f, 0.24f),
                },
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.45f,
            // AgX handles bright highlights and saturated colours more like a camera does.
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.0f,
            // Real-time global illumination: light bounces off the floor, counter and walls.
            SdfgiEnabled = true,
            SdfgiUseOcclusion = true,
            SsaoEnabled = true,
            SsaoIntensity = 2.5f,
            SsilEnabled = true, // bounce light from nearby surfaces
            SsrEnabled = true,  // reflections in the glossy counter top, tiles and mugs
            GlowEnabled = true,
            // Richer, warmer colours (Pikmin 4 style): more saturation and contrast.
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.15f,
            AdjustmentContrast = 1.05f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55f, 35f, 0f),
            LightColor = new Color(1f, 0.9f, 0.75f),
            LightEnergy = 1.6f,
            ShadowEnabled = true,
            // Soft shadows that get blurrier further from what casts them, like real sunlight.
            LightAngularDistance = 1.5f,
            ShadowBlur = 1.2f,
            DirectionalShadowMaxDistance = 80f,
        };
        AddChild(sun);

        // Narrow field of view plus near/far depth-of-field blur = the "tilt-shift" miniature look.
        _camera = new Camera3D
        {
            Fov = 35f,
            Far = 300f,
            Attributes = new CameraAttributesPractical
            {
                DofBlurNearEnabled = true,
                DofBlurNearDistance = 24f,
                DofBlurNearTransition = 10f,
                DofBlurFarEnabled = true,
                DofBlurFarDistance = 46f,
                DofBlurFarTransition = 25f,
                DofBlurAmount = 0.06f,
            },
        };
        AddChild(_camera);
        _camera.LookAtFromPosition(CameraOffset, Vector3.Zero, Vector3.Up);
        _camera.MakeCurrent();
        return (environment, sun);
    }
}
