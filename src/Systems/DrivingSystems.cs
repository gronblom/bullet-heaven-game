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

        World.Query(in Query, (ref Car car) =>
        {
            car.Throttle = throttle;
            car.Steer = steer;
            car.Handbrake = handbrake;
        });
    }
}

/// <summary>
/// Arcade top-down car handling in the spirit of Micro Machines: velocity is split into a
/// forward and a sideways part each frame. Throttle drives the forward part, grip bleeds off the
/// sideways part. Turning rotates the nose faster than the velocity follows, so low grip
/// (handbrake) gives long, controllable drifts.
/// </summary>
public sealed class CarPhysicsSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Car, CarStats, Velocity>();

    public override void Update(float dt)
    {
        World.Query(in Query, (ref Car car, ref CarStats stats, ref Velocity velocity) =>
        {
            if (car.FallTimer > 0f)
                return; // no control in mid-air

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
/// Pushes cars and enemies out of props. The car bounces off them, and drives off the table edge
/// (falls, then respawns at its last safe spot with a damage penalty). Enemies stay on the table.
/// </summary>
public sealed class CollisionSystem(World world, GameState state) : GameSystem(world, state)
{
    public const float FallDuration = 1.1f;
    private const float FallDamage = 15f;
    private const float Restitution = 0.45f;

    private static readonly QueryDescription ObstacleQuery = new QueryDescription().WithAll<Position, Obstacle>();
    private static readonly QueryDescription CarQuery = new QueryDescription().WithAll<Car, Position, Velocity, Radius, Health>();
    private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy, Position, Radius>();

    private (Vector2 Position, Obstacle Obstacle)[] _obstacles;

    public override void Update(float dt)
    {
        _obstacles ??= CollectObstacles(); // props are static, gather once

        World.Query(in CarQuery, (ref Car car, ref Position position, ref Velocity velocity, ref Radius radius, ref Health health) =>
        {
            if (car.FallTimer > 0f)
            {
                car.FallTimer -= dt;
                if (car.FallTimer <= 0f)
                {
                    position.Value = car.LastSafePosition;
                    car.Heading = car.LastSafeHeading;
                    velocity.Value = Vector2.Zero;
                    health.Current -= FallDamage;
                    health.Invulnerable = 1.5f;
                    State.ShowMessage("Fell off the table!", 1.5f);
                }
                return;
            }

            if (!State.Bounds.HasPoint(position.Value))
            {
                car.FallTimer = FallDuration;
                return;
            }

            foreach (var (obstaclePos, obstacle) in _obstacles)
            {
                if (Resolve(ref position.Value, radius.Value, obstaclePos, obstacle, out var normal))
                {
                    float into = velocity.Value.Dot(normal);
                    if (into < 0f)
                        velocity.Value -= (1f + Restitution) * into * normal;
                }
            }

            // Remember a spot comfortably away from the edge to respawn at.
            if (State.Bounds.Grow(-4f).HasPoint(position.Value))
            {
                car.LastSafePosition = position.Value;
                car.LastSafeHeading = car.Heading;
            }
        });

        var inner = State.Bounds.Grow(-0.5f);
        World.Query(in EnemyQuery, (ref Position position, ref Radius radius) =>
        {
            foreach (var (obstaclePos, obstacle) in _obstacles)
                Resolve(ref position.Value, radius.Value, obstaclePos, obstacle, out _);

            position.Value = position.Value.Clamp(inner.Position, inner.End);
        });
    }

    private (Vector2, Obstacle)[] CollectObstacles()
    {
        var list = new System.Collections.Generic.List<(Vector2, Obstacle)>();
        World.Query(in ObstacleQuery, (ref Position position, ref Obstacle obstacle) => list.Add((position.Value, obstacle)));
        return list.ToArray();
    }

    /// <summary>Moves a circle out of an obstacle. Returns true with the push-out normal if they overlapped.</summary>
    private static bool Resolve(ref Vector2 point, float radius, Vector2 center, in Obstacle obstacle, out Vector2 normal)
    {
        normal = Vector2.Zero;
        if (obstacle.Shape == ObstacleShape.Circle)
        {
            var diff = point - center;
            float minDist = radius + obstacle.Radius;
            float distSq = diff.LengthSquared();
            if (distSq >= minDist * minDist)
                return false;

            float dist = MathF.Sqrt(distSq);
            normal = dist > 1e-4f ? diff / dist : Vector2.Right;
            point = center + normal * minDist;
            return true;
        }

        var min = center - obstacle.HalfExtents;
        var max = center + obstacle.HalfExtents;
        var closest = point.Clamp(min, max);
        var delta = point - closest;
        float dSq = delta.LengthSquared();

        if (dSq > 1e-8f)
        {
            if (dSq >= radius * radius)
                return false;
            float d = MathF.Sqrt(dSq);
            normal = delta / d;
            point = closest + normal * radius;
            return true;
        }

        // Centre is inside the box: push out along the shallowest axis.
        float left = point.X - min.X, right = max.X - point.X;
        float top = point.Y - min.Y, bottom = max.Y - point.Y;
        float smallest = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
        if (smallest == left) { normal = Vector2.Left; point.X = min.X - radius; }
        else if (smallest == right) { normal = Vector2.Right; point.X = max.X + radius; }
        else if (smallest == top) { normal = Vector2.Up; point.Y = min.Y - radius; }
        else { normal = Vector2.Down; point.Y = max.Y + radius; }
        return true;
    }
}
