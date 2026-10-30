using System;
using Arch.Core;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Systems;

/// <summary>Copies this frame's <see cref="GameState.Input"/> (filled in by the engine front-end) into the
/// player's Runner or Car component.</summary>
public sealed class PlayerInputSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription CarQuery = new QueryDescription().WithAll<PlayerTag, Car>();
    private static readonly QueryDescription RunnerQuery = new QueryDescription().WithAll<PlayerTag, Runner>();

    public override void Update(float dt)
    {
        var input = State.Input;
        World.Query(in RunnerQuery, (ref Runner runner) =>
        {
            runner.Move = input.Move;
            runner.JumpPressed = input.JumpPressed;
            runner.JumpHeld = input.JumpHeld;
        });
        World.Query(in CarQuery, (ref Car car) =>
        {
            car.Throttle = input.Throttle;
            car.Steer = input.Steer;
            car.Handbrake = input.Handbrake;
            car.Jump = input.CarJump;
        });
    }
}

/// <summary>
/// Direct control: the character accelerates towards the pushed direction and stops quickly
/// when released. Jumps get a short input buffer, coyote time after running off a ledge, and
/// variable height (let go early for a short hop).
/// </summary>
public sealed class RunnerPhysicsSystem(World world, GameState state) : GameSystem(world, state)
{
    private const float JumpBufferTime = 0.12f;
    private const float CoyoteTime = 0.1f;
    private const float ShortHopSpeed = 3.5f;
    public const float GlideFallSpeed = 1.8f;
    public const float GlideSpeedScale = 1.2f;

    private static readonly QueryDescription Query = new QueryDescription().WithAll<Runner, RunnerStats, Velocity, Elevation>();

    public override void Update(float dt)
    {
        World.Query(in Query, (ref Runner runner, ref RunnerStats stats, ref Velocity velocity, ref Elevation elevation) =>
        {
            runner.JumpBuffer = runner.JumpPressed ? JumpBufferTime : MathF.Max(0f, runner.JumpBuffer - dt);
            if (elevation.Grounded)
                runner.Jumped = false;

            bool canJump = elevation.Grounded || (!runner.Jumped && elevation.AirTime < CoyoteTime && elevation.VerticalSpeed <= 0f);
            if (runner.JumpBuffer > 0f && canJump)
            {
                elevation.VerticalSpeed = stats.JumpSpeed;
                elevation.Grounded = false;
                runner.Jumped = true;
                runner.JumpBuffer = 0f;
            }
            else if (runner.Jumped && !runner.JumpHeld && elevation.VerticalSpeed > ShortHopSpeed)
            {
                elevation.VerticalSpeed = ShortHopSpeed; // released early: short hop
            }

            // Parachute: hold jump while falling to float down slowly and steer further.
            runner.Gliding = State.HasParachute && !elevation.Grounded && runner.JumpHeld && elevation.VerticalSpeed < 0f;
            elevation.MaxFallSpeed = runner.Gliding ? GlideFallSpeed : 0f;

            float speed = runner.Gliding ? stats.MaxSpeed * GlideSpeedScale : stats.MaxSpeed;
            if (elevation.Grounded)
                speed *= State.PlayerSlow; // wading through spilled liquid
            var target = runner.Move.LimitLength(1f) * speed;
            float rate = runner.Gliding ? stats.AirAcceleration * 1.5f
                : !elevation.Grounded ? stats.AirAcceleration
                : runner.Move == Vector2.Zero ? stats.Friction
                : stats.Acceleration;
            velocity.Value = velocity.Value.MoveToward(target, rate * dt);

            if (runner.Move.LengthSquared() > 0.01f)
                runner.Facing = Mathf.LerpAngle(runner.Facing, runner.Move.Angle(), 1f - MathF.Exp(-18f * dt));
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
/// Horizontal collisions against the terrain and the room walls. Whether a prop is a wall depends
/// on height: the player steps onto anything up to <see cref="StepUp"/> above them, so jumps and
/// ramps get them onto blocks and the counter. Bugs that hit a wall climb it instead.
/// </summary>
public sealed class CollisionSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float StepUp = 0.35f;
    private const float CarRestitution = 0.45f;

    private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Radius, Elevation>();

    public override void Update(float dt)
    {
        var terrain = State.Terrain;
        var player = State.Player;
        ref var position = ref World.Get<Position>(player);
        ref var velocity = ref World.Get<Velocity>(player);
        float radius = World.Get<Radius>(player).Value;
        float height = World.Get<Elevation>(player).Height;
        float restitution = World.Has<Car>(player) ? CarRestitution : 0f; // the runner just slides along walls

        var normals = terrain.ResolveWalls(ref position.Value, radius, height, StepUp);
        normals += ClampToRoom(ref position.Value, radius);
        if (normals != Vector2.Zero)
        {
            var normal = normals.Normalized();
            float into = velocity.Value.Dot(normal);
            if (into < 0f)
                velocity.Value -= (1f + restitution) * into * normal;
        }

        World.Query(in EnemyQuery, (ref Enemy enemy, ref Position p, ref Radius r, ref Elevation elevation) =>
        {
            var hit = terrain.ResolveWalls(ref p.Value, r.Value, elevation.Height, StepUp, out float wallTop);
            ClampToRoom(ref p.Value, r.Value);

            elevation.Climbing = false;
            if (hit == Vector2.Zero)
                return;

            // Pressed against something taller: scale it, then hop over the lip onto the top.
            elevation.Height += enemy.ClimbSpeed * dt;
            if (elevation.Height >= wallTop - StepUp)
            {
                p.Value -= hit.Normalized() * (r.Value + 0.05f);
                elevation.Height = wallTop;
                elevation.VerticalSpeed = 0f;
                elevation.Grounded = true;
            }
            else
            {
                elevation.Climbing = true;
            }
        });
    }

    private Vector2 ClampToRoom(ref Vector2 point, float radius)
    {
        var inner = State.Bounds.Grow(-radius);
        var clamped = point.Clamp(inner.Position, inner.End);
        var normal = clamped - point; // points back into the room
        point = clamped;
        return normal == Vector2.Zero ? Vector2.Zero : normal.Normalized();
    }
}

/// <summary>
/// Gravity, ground following and landing for everything with an Elevation. On the ground,
/// height tracks the surface and its rate of change becomes vertical speed, so running or
/// driving off the top of a ramp launches you. Walking off a ledge drops you.
/// </summary>
public sealed class VerticalPhysicsSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float Gravity = 22f;
    private const float SnapDown = 0.25f; // follow drops smaller than this (walking down slopes)

    private static readonly QueryDescription Query = new QueryDescription().WithAll<Position, Elevation>();

    public override void Update(float dt)
    {
        var terrain = State.Terrain;

        World.Query(in Query, (ref Position position, ref Elevation elevation) =>
        {
            elevation.LandingImpact = 0f;
            if (elevation.Climbing)
            {
                elevation.Grounded = false;
                elevation.VerticalSpeed = 0f;
                elevation.AirTime = 0f;
                return;
            }

            float ground = terrain.GroundHeight(position.Value);
            if (elevation.Grounded)
            {
                if (ground < elevation.Height - SnapDown)
                {
                    elevation.Grounded = false; // went over an edge; keep the current vertical speed
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
            if (elevation.MaxFallSpeed > 0f && elevation.VerticalSpeed < -elevation.MaxFallSpeed)
                elevation.VerticalSpeed = MathF.Max(-elevation.MaxFallSpeed, elevation.VerticalSpeed + 3f * Gravity * dt);
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
    }
}
