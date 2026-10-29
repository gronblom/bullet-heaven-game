using System;
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
/// into the visuals (player node, MultiMesh batches, camera, HUD).
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Tick in the Inspector (select the Main node) to play as the toy car instead of the runner.</summary>
    [Export] public bool DriveCar { get; set; }

    private static readonly Vector3 CameraOffset = new(0f, 32f, 18f);
    private static readonly Color AntColor = new(0.45f, 0.08f, 0.05f);
    private static readonly Color BeetleColor = new(0.1f, 0.3f, 0.25f);

    private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Velocity, Radius, Elevation>();
    private static readonly QueryDescription BulletQuery = new QueryDescription().WithAll<Bullet, Position>();
    private static readonly QueryDescription GemQuery = new QueryDescription().WithAll<XpGem, Position>();

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
            new Elevation { Grounded = true },
            Weapon.Default);

        if (DriveCar)
        {
            _world.Add(_state.Player, new Radius(0.6f), new Car { Heading = facingUp }, CarStats.Default);
            _carView = new CarView(new Color(0.9f, 0.15f, 0.1f));
            AddChild(_carView);
        }
        else
        {
            var weapon = Weapon.Default;
            weapon.Cooldown = 0.35f; // no ramming on foot, so a slightly quicker gun
            _world.Set(_state.Player, weapon);
            _world.Add(_state.Player, new Radius(0.35f), new Runner { Facing = facingUp }, RunnerStats.Default);
            _runnerView = new RunnerView();
            AddChild(_runnerView);
        }

        // One batch per enemy kind so each can use its own imported model.
        _antRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 },
            Materials.InstanceColored(0.35f), ModelLibrary.Ant);
        _beetleRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 },
            Materials.InstanceColored(0.35f), ModelLibrary.Beetle);
        _bulletRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 8, Rings = 4 },
            Materials.Glow(new Color(1f, 0.85f, 0.3f)), castShadows: false);
        _gemRenderer = new InstancedRenderer(this, new BoxMesh(), Materials.Glow(new Color(0.3f, 1f, 0.5f), 1.5f),
            ModelLibrary.Gem, castShadows: false);

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

    private void SyncVisuals(float dt)
    {
        var player = _state.Player;
        var position = _world.Get<Position>(player);
        var velocity = _world.Get<Velocity>(player);
        var elevation = _world.Get<Elevation>(player);
        var health = _world.Get<Health>(player);
        if (DriveCar)
            _carView.Sync(position, _world.Get<Car>(player), elevation, health, dt);
        else
            _runnerView.Sync(position, velocity, _world.Get<Runner>(player), elevation, health, dt);

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
        var terrain = _state.Terrain;

        int enemyCount = 0;
        _antRenderer.Begin();
        _beetleRenderer.Begin();
        _world.Query(in EnemyQuery, (ref Enemy enemy, ref Position p, ref Velocity v, ref Radius r, ref Elevation e) =>
        {
            float yaw = v.Value.LengthSquared() > 0.01f ? -v.Value.Angle() : 0f;
            var renderer = enemy.Kind == EnemyKind.Beetle ? _beetleRenderer : _antRenderer;
            var color = enemy.HitFlash > 0f ? Colors.White : enemy.Kind == EnemyKind.Beetle ? BeetleColor : AntColor;
            float radius = r.Value;
            float pop = 1f + enemy.HitFlash * 2f; // hit feedback that also works for imported models
            float ground = e.Height;

            if (renderer.UsesModel) // models are fitted to length 1 and stand on the ground
                renderer.Add(new Vector3(p.Value.X, ground, p.Value.Y), yaw, Vector3.One * radius * 2f * pop, color);
            else // squashed sphere centred above the ground
                renderer.Add(new Vector3(p.Value.X, ground + radius * 0.6f, p.Value.Y), yaw,
                    new Vector3(radius * 1.4f, radius * 0.6f, radius) * pop, color);
            enemyCount++;
        });
        _antRenderer.End();
        _beetleRenderer.End();

        _bulletRenderer.Begin();
        _world.Query(in BulletQuery, (ref Bullet b, ref Position p) =>
            _bulletRenderer.Add(new Vector3(p.Value.X, b.Height, p.Value.Y), 0f, new Vector3(0.18f, 0.18f, 0.18f), Colors.White));
        _bulletRenderer.End();

        float spin = _state.Time * 3f;
        _gemRenderer.Begin();
        _world.Query(in GemQuery, (ref Position p) =>
        {
            var at = new Vector3(p.Value.X, terrain.GroundHeight(p.Value) + 0.35f, p.Value.Y);
            _gemRenderer.Add(at, spin + p.Value.X, new Vector3(0.3f, 0.3f, 0.3f), Colors.White);
        });
        _gemRenderer.End();

        _hud.Sync(_state, health, enemyCount, velocity.Value.Length(), dt);
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
                    SkyTopColor = new Color(0.55f, 0.7f, 0.9f),
                    SkyHorizonColor = new Color(0.9f, 0.85f, 0.75f),
                    GroundHorizonColor = new Color(0.9f, 0.85f, 0.75f),
                    GroundBottomColor = new Color(0.4f, 0.38f, 0.35f),
                },
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.8f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            SsaoEnabled = true,
            SsilEnabled = true, // bounce light from nearby surfaces
            SsrEnabled = true,  // reflections in the glossy counter top, tiles and mugs
            GlowEnabled = true,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55f, 35f, 0f),
            LightColor = new Color(1f, 0.95f, 0.85f),
            LightEnergy = 1.2f,
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
