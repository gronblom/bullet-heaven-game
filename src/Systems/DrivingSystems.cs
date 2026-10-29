using System;
using Arch.Core;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Systems;

/// <summary>Reads the input map into the player's Car component.</summary>
public sealed class PlayerInputSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<PlayerTag, Car>();

    public override void Update(float dt)
    {
        float throttle = Input.GetActionStrength(GameInput.Accelerate) - Input.GetActionStrength(GameInput.Brake);
        float steer = Input.GetAxis(GameInput.SteerLeft, GameInput.SteerRight);
        bool handbrake = Input.IsActionPressed(GameInput.Handbrake);
        bool jump = Input.IsActionJustPressed(GameInput.Jump);

        World.Query(in Query, (ref Car car) =>
        {
            car.Throttle = throttle;
            car.Steer = steer;
            car.Handbrake = handbrake;
            car.Jump = jump;
        });
    }
}

/// <summary>
/// Arcade top-down car handling in the spirit of Micro Machines: velocity is split into a
/// forward and a sideways part each frame. Throttle drives the forward part, grip bleeds off the
/// sideways part. Turning rotates the nose faster than the velocity follows, so low grip
/// (handbrake) gives long, controllable drifts. No control while airborne.
/// </summary>
public sealed class CarPhysicsSystem(World world, GameState state) : GameSystem(world, state)
{
    private const float JumpCooldown = 0.35f;
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Car, CarStats, Velocity, Elevation>();

    public override void Update(float dt)
    {
        World.Query(in Query, (ref Car car, ref CarStats stats, ref Velocity velocity, ref Elevation elevation) =>
        {
            car.JumpCooldown = MathF.Max(0f, car.JumpCooldown - dt);

            if (!elevation.Grounded)
            {
                velocity.Value *= MathF.Exp(-0.15f * dt); // a touch of air drag, otherwise ballistic
                return;
            }

            if (car.Jump && car.JumpCooldown <= 0f)
            {
                elevation.VerticalSpeed = stats.JumpSpeed;
                elevation.Grounded = false;
                car.JumpCooldown = JumpCooldown;
            }

            var forward = Vector2.FromAngle(car.Heading);
            var side = new Vector2(-forward.Y, forward.X);
            float forwardSpeed = velocity.Value.Dot(forward);
            float sideSpeed = velocity.Value.Dot(side);

            if (car.Throttle > 0f)
            {
                forwardSpeed += stats.Acceleration * car.Throttle * dt;
            }
            else if (car.Throttle < 0f)
            {
                // Brake while rolling forward, then reverse.
                float force = forwardSpeed > 0.5f ? stats.BrakeForce : stats.Acceleration * 0.6f;
                forwardSpeed += force * car.Throttle * dt;
            }
            else
            {
                forwardSpeed *= MathF.Exp(-stats.RollingDrag * dt);
            }

            if (car.Handbrake)
                forwardSpeed *= MathF.Exp(-1.2f * dt);

            forwardSpeed = Math.Clamp(forwardSpeed, -stats.MaxReverse, stats.MaxSpeed);

            // Steering needs speed, and flips when reversing.
            float steerFactor = Math.Clamp(forwardSpeed / (stats.MaxSpeed * 0.25f), -1f, 1f);
            float turnRate = stats.TurnRate * (car.Handbrake ? 1.35f : 1f);
            car.Heading = Mathf.Wrap(car.Heading + car.Steer * turnRate * steerFactor * dt, -Mathf.Pi, Mathf.Pi);

            float grip = car.Handbrake ? stats.HandbrakeGrip : stats.Grip;
            sideSpeed *= MathF.Exp(-grip * dt);

            // Recombine in the old frame: the new heading turns sideways speed into drift next frame.
            velocity.Value = forward * forwardSpeed + side * sideSpeed;
            car.ForwardSpeed = forwardSpeed;
            car.Slip = sideSpeed;
        });
    }
}

/// <summary>Integrates velocity for everything that moves.</summary>
public sealed class MovementSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Position, Velocity>();

    public override void Update(float dt)
    {
        World.Query(in Query, (ref Position position, ref Velocity velocity) =>
        {
            position.Value += velocity.Value * dt;
        });
    }
}

/// <summary>
/// Horizontal collisions against the terrain. Whether a prop is a wall depends on height: the car
/// can step onto anything up to <see cref="CarStepUp"/> above it, so a jump or a ramp gets it
/// onto books it would otherwise bounce off. Bugs climb anything up to <see cref="BugClimb"/>.
/// </summary>
public sealed class CollisionSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float CarStepUp = 0.35f;
    public const float BugClimb = 3.5f;
    private const float Restitution = 0.45f;

    private static readonly QueryDescription CarQuery = new QueryDescription().WithAll<Car, Position, Velocity, Radius, Elevation>();
    private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Radius>();

    public override void Update(float dt)
    {
        var terrain = State.Terrain;

        World.Query(in CarQuery, (ref Car car, ref Position position, ref Velocity velocity, ref Radius radius, ref Elevation elevation) =>
        {
            if (elevation.Height < -0.5f)
                return; // already dropping past the table edge

            var normals = terrain.ResolveWalls(ref position.Value, radius.Value, elevation.Height, CarStepUp);
            if (normals != Vector2.Zero)
            {
                var normal = normals.Normalized();
                float into = velocity.Value.Dot(normal);
                if (into < 0f)
                    velocity.Value -= (1f + Restitution) * into * normal;
            }

            // Remember a spot on the table floor, comfortably away from the edge, to respawn at.
            if (elevation.Grounded && elevation.Height < 0.01f && State.Bounds.Grow(-4f).HasPoint(position.Value))
                car.LastSafePosition = position.Value;
        });

        var inner = State.Bounds.Grow(-0.5f);
        World.Query(in EnemyQuery, (ref Position position, ref Radius radius) =>
        {
            terrain.ResolveWalls(ref position.Value, radius.Value, 0f, BugClimb);
            position.Value = position.Value.Clamp(inner.Position, inner.End);
        });
    }
}

/// <summary>
/// Gravity, ground following and landing. On the ground, height tracks the surface and the rate
/// of change becomes vertical speed, so driving off the top of a ramp launches you. Past the
/// table edge the ground drops away; the car falls and respawns with a damage penalty.
/// </summary>
public sealed class VerticalPhysicsSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float Gravity = 22f;
    private const float SnapDown = 0.25f;   // follow drops smaller than this (driving down slopes)
    private const float RespawnHeight = -12f;
    private const float FallDamage = 15f;

    private static readonly QueryDescription Query = new QueryDescription().WithAll<Position, Elevation>();
    private static readonly QueryDescription CarQuery = new QueryDescription().WithAll<Car, Position, Velocity, Elevation, Health>();

    public override void Update(float dt)
    {
        var terrain = State.Terrain;

        World.Query(in Query, (ref Position position, ref Elevation elevation) =>
        {
            float ground = terrain.GroundHeight(position.Value);
            elevation.LandingImpact = 0f;

            if (elevation.Grounded)
            {
                if (ground < elevation.Height - SnapDown)
                {
                    elevation.Grounded = false; // drove off an edge; keep the current vertical speed
                }
                else
                {
                    elevation.VerticalSpeed = Math.Clamp((ground - elevation.Height) / dt, -12f, 12f);
                    elevation.Height = ground;
                    return;
                }
            }

            elevation.AirTime += dt;
            elevation.VerticalSpeed -= Gravity * dt;
            elevation.Height += elevation.VerticalSpeed * dt;
            if (elevation.Height <= ground)
            {
                elevation.LandingImpact = -elevation.VerticalSpeed;
                elevation.Height = ground;
                elevation.VerticalSpeed = 0f;
                elevation.Grounded = true;
                elevation.AirTime = 0f;
            }
        });

        World.Query(in CarQuery, (ref Car car, ref Position position, ref Velocity velocity, ref Elevation elevation, ref Health health) =>
        {
            if (elevation.Height > RespawnHeight)
                return;

            position.Value = car.LastSafePosition;
            car.Heading = (State.Bounds.GetCenter() - position.Value).Angle(); // face away from the edge
            velocity.Value = Vector2.Zero;
            elevation = new Elevation { Grounded = true };
            health.Current -= FallDamage;
            health.Invulnerable = 1.5f;
            State.ShowMessage("Fell off the table!", 1.5f);
        });
    }
}
