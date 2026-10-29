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
/// into the visuals (car node, MultiMesh batches, camera, HUD).
/// </summary>
public partial class Main : Node3D
{
    private static readonly Vector3 CameraOffset = new(0f, 34f, 22f);
    private static readonly Color AntColor = new(0.45f, 0.08f, 0.05f);
    private static readonly Color BeetleColor = new(0.1f, 0.3f, 0.25f);

    private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Velocity, Radius>();
    private static readonly QueryDescription BulletQuery = new QueryDescription().WithAll<Bullet, Position>();
    private static readonly QueryDescription GemQuery = new QueryDescription().WithAll<XpGem, Position>();

    private World _world;
    private GameState _state;
    private GameSystem[] _systems;

    private CarView _carView;
    private Camera3D _camera;
    private Vector3 _cameraTarget;
    private Hud _hud;
    private InstancedRenderer _enemyRenderer;
    private InstancedRenderer _bulletRenderer;
    private InstancedRenderer _gemRenderer;

    public override void _Ready()
    {
        GameInput.Register();

        _world = World.Create();
        _state = new GameState(KitchenTable.Bounds);

        BuildEnvironment();
        KitchenTable.Build(this, _world);

        var start = new Vector2(0f, 8f);
        const float startHeading = -Mathf.Pi / 2f; // nose pointing up the screen
        _state.Player = _world.Create(
            new PlayerTag(),
            new Position(start),
            new Velocity(),
            new Radius(0.6f),
            new Health(100f),
            new Car { Heading = startHeading, LastSafePosition = start, LastSafeHeading = startHeading },
            CarStats.Default,
            Weapon.Default);

        _carView = new CarView(new Color(0.9f, 0.15f, 0.1f));
        AddChild(_carView);

        _enemyRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 },
            Materials.InstanceColored(0.35f));
        _bulletRenderer = new InstancedRenderer(this, new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 8, Rings = 4 },
            Materials.Glow(new Color(1f, 0.85f, 0.3f)), castShadows: false);
        _gemRenderer = new InstancedRenderer(this, new BoxMesh(), Materials.Glow(new Color(0.3f, 1f, 0.5f), 1.5f), castShadows: false);

        _hud = new Hud();
        AddChild(_hud);

        // Order matters: input -> physics -> collisions -> spatial index -> combat -> cleanup.
        _systems = new GameSystem[]
        {
            new PlayerInputSystem(_world, _state),
            new CarPhysicsSystem(_world, _state),
            new EnemySteeringSystem(_world, _state),
            new MovementSystem(_world, _state),
            new CollisionSystem(_world, _state),
            new EnemyGridSystem(_world, _state),
            new RamSystem(_world, _state),
            new WeaponSystem(_world, _state),
            new BulletSystem(_world, _state),
            new EnemyDeathSystem(_world, _state),
            new PickupSystem(_world, _state),
            new EnemySpawnSystem(_world, _state),
        };

        _cameraTarget = new Vector3(start.X, 0f, start.Y);
        _state.ShowMessage("Ram the bugs!\nWASD / arrows to drive, Space to drift", 4f);
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
        var car = _world.Get<Car>(player);
        var health = _world.Get<Health>(player);
        _carView.Sync(position, car, health, dt);

        // Camera never rotates (like Micro Machines); it trails the car with some look-ahead.
        var lookAhead = new Vector3(velocity.Value.X, 0f, velocity.Value.Y) * 0.45f;
        var desired = new Vector3(position.Value.X, 0f, position.Value.Y) + lookAhead;
        _cameraTarget = _cameraTarget.Lerp(desired, 1f - MathF.Exp(-4f * dt));
        _camera.Position = _cameraTarget + CameraOffset;

        int enemyCount = 0;
        _enemyRenderer.Begin();
        _world.Query(in EnemyQuery, (ref Enemy enemy, ref Position p, ref Velocity v, ref Radius r) =>
        {
            float yaw = v.Value.LengthSquared() > 0.01f ? -v.Value.Angle() : 0f;
            var color = enemy.HitFlash > 0f ? Colors.White : enemy.Kind == EnemyKind.Beetle ? BeetleColor : AntColor;
            float radius = r.Value;
            _enemyRenderer.Add(new Vector3(p.Value.X, radius * 0.6f, p.Value.Y), yaw,
                new Vector3(radius * 1.4f, radius * 0.6f, radius), color);
            enemyCount++;
        });
        _enemyRenderer.End();

        _bulletRenderer.Begin();
        _world.Query(in BulletQuery, (ref Position p) =>
            _bulletRenderer.Add(new Vector3(p.Value.X, 0.5f, p.Value.Y), 0f, new Vector3(0.18f, 0.18f, 0.18f), Colors.White));
        _bulletRenderer.End();

        float spin = _state.Time * 3f;
        _gemRenderer.Begin();
        _world.Query(in GemQuery, (ref Position p) =>
            _gemRenderer.Add(new Vector3(p.Value.X, 0.35f, p.Value.Y), spin + p.Value.X, new Vector3(0.3f, 0.3f, 0.3f), Colors.White));
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
            GlowEnabled = true,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55f, 35f, 0f),
            LightColor = new Color(1f, 0.95f, 0.85f),
            LightEnergy = 1.2f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 90f,
        });

        // Narrow field of view plus near/far depth-of-field blur = the "tilt-shift" miniature look.
        _camera = new Camera3D
        {
            Fov = 35f,
            Far = 300f,
            Attributes = new CameraAttributesPractical
            {
                DofBlurNearEnabled = true,
                DofBlurNearDistance = 28f,
                DofBlurNearTransition = 10f,
                DofBlurFarEnabled = true,
                DofBlurFarDistance = 52f,
                DofBlurFarTransition = 25f,
                DofBlurAmount = 0.06f,
            },
        };
        AddChild(_camera);
        _camera.LookAtFromPosition(CameraOffset, Vector3.Zero, Vector3.Up);
        _camera.MakeCurrent();
    }
}
