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

    private static readonly Vector3 CameraOffset = new(0f, 32f, 18f);
    private static readonly Color AntColor = new(0.45f, 0.08f, 0.05f);
    private static readonly Color BeetleColor = new(0.1f, 0.3f, 0.25f);

    private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Velocity, Radius, Elevation>();
    private static readonly QueryDescription BulletQuery = new QueryDescription().WithAll<Bullet, Position, Velocity>();
    private static readonly QueryDescription GemQuery = new QueryDescription().WithAll<XpGem, Position>();
    private static readonly QueryDescription OrbQuery = new QueryDescription().WithAll<LightningOrb, Position>();
    private static readonly QueryDescription ShellQuery = new QueryDescription().WithAll<Shell, Position>();
    private static readonly QueryDescription BlastQuery = new QueryDescription().WithAll<Blast, Position>();

    private World _world;
    private GameState _state;
    private GameSystem[] _systems;

    private CarView _carView;
    private RunnerView _runnerView;
    private OcclusionFader _fader;
    private Camera3D _camera;
    private Vector3 _cameraTarget;
    private Hud _hud;
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

        _world = World.Create();
        _state = new GameState(Kitchen.Bounds);
        _fader = new OcclusionFader();

        BuildEnvironment();
        Kitchen.Build(this, _world, _fader);
        _state.Terrain = Terrain.FromWorld(_world, Kitchen.Bounds);

        var start = Kitchen.PlayerStart;
        const float facingUp = -Mathf.Pi / 2f; // up the screen
        _state.Player = _world.Create(
            new PlayerTag(),
            new Position(start),
            new Velocity(),
            new Health(100f),
            new Elevation { Grounded = true });
        Arsenal.Grant(_world, WeaponKind.Minigun);

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

        _hud = new Hud();
        AddChild(_hud);

        // Order matters: input -> physics -> collisions -> spatial index -> combat -> cleanup.
        _systems = new GameSystem[]
        {
            new PlayerInputSystem(_world, _state),
            new RunnerPhysicsSystem(_world, _state),
            new CarPhysicsSystem(_world, _state),
            new EnemySteeringSystem(_world, _state),
            new MovementSystem(_world, _state),
            new CollisionSystem(_world, _state),
            new VerticalPhysicsSystem(_world, _state),
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
        _state.ShowMessage(DriveCar
            ? "Ram the bugs!\nWASD / arrows to drive, Shift to drift, Space to jump"
            : "Stomp the bugs!\nWASD / arrows / stick to run, Space / A to jump\nClimb the stools or the toy blocks to reach the counter", 5f);
    }

    public override void _Process(double delta)
    {
        float dt = Math.Min((float)delta, 1f / 20f); // avoid huge steps after hitches

        if (_state.GameOver)
        {
            if (Input.IsActionJustPressed(GameInput.Restart))
            {
                GetTree().ReloadCurrentScene();
                return;
            }
        }
        else
        {
            DebugWeaponKeys();
            _state.Time += dt;
            foreach (var system in _systems)
                system.Update(dt);
            _state.FlushDestroyed(_world);
        }

        SyncVisuals(dt);
    }

    public override void _ExitTree()
    {
        if (_world != null)
            World.Destroy(_world);
        _world = null;
    }

    /// <summary>Number keys 1-3 add or level up a weapon, for trying them out without grinding.</summary>
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
        float minigunSpin = weapons.Find(w => w.Kind == WeaponKind.Minigun).Spin;

        if (DriveCar)
        {
            _carView.Sync(position, _world.Get<Car>(player), elevation, health, dt);
        }
        else
        {
            _runnerView.ShowWeapons(weapons.Exists(w => w.Kind == WeaponKind.Minigun), weapons.Exists(w => w.Kind == WeaponKind.LightningWand));
            _runnerView.Sync(position, velocity, _world.Get<Runner>(player), elevation, health, minigunSpin, dt);
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

        _hud.Sync(_state, health, enemyCount, velocity.Value.Length(), weapons, dt);
    }

    private int DrawBugs()
    {
        int count = 0;
        _antRenderer.Begin();
        _beetleRenderer.Begin();
        _world.Query(in EnemyQuery, (ref Enemy enemy, ref Position p, ref Velocity v, ref Radius r, ref Elevation e) =>
        {
            var heading = v.Value.LengthSquared() > 0.01f ? v.Value.Normalized() : Vector2.Right;
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

    private void BuildEnvironment()
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
            AmbientLightEnergy = 0.5f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 0.95f,
            SsaoEnabled = true,
            SsaoIntensity = 2.5f,
            SsilEnabled = true, // bounce light from nearby surfaces
            SsrEnabled = true,  // reflections in the glossy counter top, tiles and mugs
            GlowEnabled = true,
            // Richer, warmer colours (Pikmin 4 style): more saturation and contrast.
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.25f,
            AdjustmentContrast = 1.08f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55f, 35f, 0f),
            LightColor = new Color(1f, 0.9f, 0.75f),
            LightEnergy = 1.35f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 80f,
        });

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
    }
}
