using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Systems;

/// <summary>Spawns enemies in a ring just off-screen. Spawn rate and toughness grow with time,
/// and every 30 s a swarm pours in from one direction.</summary>
public sealed class EnemySpawnSystem(World world, GameState state) : GameSystem(world, state)
{
    private const int MaxEnemies = 1500;
    private const float SwarmInterval = 30f;
    private static readonly QueryDescription EnemyQuery = new QueryDescription().WithAll<Enemy>();

    private float _accumulator;
    private float _nextSwarm = SwarmInterval;

    public override void Update(float dt)
    {
        var playerPos = World.Get<Position>(State.Player).Value;
        int alive = World.CountEntities(in EnemyQuery);

        _accumulator += dt * (1.5f + State.Time * 0.08f);
        while (_accumulator >= 1f && alive < MaxEnemies)
        {
            _accumulator -= 1f;
            float angle = (float)State.Rng.NextDouble() * Mathf.Tau;
            Spawn(PickSpawnPoint(playerPos, angle), PickKind());
            alive++;
        }

        if (State.Time >= _nextSwarm)
        {
            _nextSwarm += SwarmInterval;
            int count = 30 + (int)(State.Time * 0.5f);
            float baseAngle = (float)State.Rng.NextDouble() * Mathf.Tau;
            for (int i = 0; i < count && alive < MaxEnemies; i++, alive++)
            {
                float angle = baseAngle + ((float)State.Rng.NextDouble() - 0.5f) * 0.6f;
                Spawn(PickSpawnPoint(playerPos, angle), EnemyKind.Ant);
            }
            State.ShowMessage("Here comes the swarm!");
        }
    }

    private EnemyKind PickKind() =>
        State.Time > 45f && State.Rng.NextDouble() < 0.15 ? EnemyKind.Beetle : EnemyKind.Ant;

    private Vector2 PickSpawnPoint(Vector2 playerPos, float angle)
    {
        var inner = State.Bounds.Grow(-1f);
        var point = Vector2.Zero;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float distance = 26f + (float)State.Rng.NextDouble() * 6f;
            point = (playerPos + Vector2.FromAngle(angle) * distance).Clamp(inner.Position, inner.End);
            if (point.DistanceTo(playerPos) > 16f)
                break;
            angle += 1.1f; // too close after clamping to the table edge, try elsewhere
        }
        return point;
    }

    private void Spawn(Vector2 position, EnemyKind kind)
    {
        float t = State.Time;
        float jitter = (float)State.Rng.NextDouble();
        var (radius, enemy, health) = kind switch
        {
            EnemyKind.Beetle => (0.75f,
                new Enemy { Kind = kind, Speed = 2.6f + jitter * 0.4f, ClimbSpeed = 1.5f, ContactDamage = 14f, XpValue = 4 },
                new Health(45f + t * 0.4f)),
            _ => (0.35f,
                new Enemy { Kind = kind, Speed = 4.2f + jitter * 0.8f, ClimbSpeed = 3f, ContactDamage = 6f, XpValue = 1 },
                new Health(10f + t * 0.12f)),
        };

        var elevation = new Elevation { Height = State.Terrain.GroundHeight(position), Grounded = true };
        World.Create(new Position(position), new Velocity(), new Radius(radius), elevation, enemy, health);
    }
}

/// <summary>Chases the player, keeping distance from neighbours so swarms spread out. Bugs head
/// straight for the player; walls in the way get climbed (see CollisionSystem).</summary>
public sealed class EnemySteeringSystem(World world, GameState state) : GameSystem(world, state)
{
    // In a dense crowd a handful of neighbours is enough to keep bugs apart, and keeps the cost flat.
    private const int MaxNeighbours = 8;
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Enemy, Position, Velocity, Radius, Elevation>();
    private readonly List<int> _neighbours = new();

    public override void Update(float dt)
    {
        var playerPos = World.Get<Position>(State.Player).Value;
        float playerRadius = World.Get<Radius>(State.Player).Value;
        var grid = State.EnemyGrid; // built last frame; only its arrays are read here
        float knockbackDecay = MathF.Exp(-6f * dt);

        World.Query(in Query, (Entity entity, ref Enemy enemy, ref Position position, ref Velocity velocity, ref Radius radius,
            ref Elevation elevation) =>
        {
            var toPlayer = playerPos - position.Value;
            float distance = toPlayer.Length();
            // Stop once touching the car, otherwise the whole swarm collapses onto one point.
            bool touching = distance < playerRadius + radius.Value * 0.8f;
            var desired = touching || distance < 1e-3f ? Vector2.Zero : toPlayer / distance * enemy.Speed;

            var push = Vector2.Zero;
            _neighbours.Clear();
            grid.Query(position.Value, radius.Value, _neighbours, MaxNeighbours);
            foreach (int i in _neighbours)
            {
                if (grid.Entities[i] == entity || MathF.Abs(grid.Heights[i] - elevation.Height) > 1f)
                    continue; // bugs on another level don't crowd each other
                var away = position.Value - grid.Positions[i];
                float minDist = radius.Value + grid.Radii[i];
                float distSq = away.LengthSquared();
                if (distSq >= minDist * minDist)
                    continue;
                if (distSq < 1e-6f)
                {
                    // Exactly stacked: split apart in a direction unique to this entity.
                    push += Vector2.FromAngle(entity.Id * 2.39996f) * minDist;
                    continue;
                }
                float d = MathF.Sqrt(distSq);
                push += away / d * (minDist - d);
            }

            velocity.Value = desired + push * 12f + enemy.Knockback;
            enemy.Knockback *= knockbackDecay;
            enemy.HitFlash = MathF.Max(0f, enemy.HitFlash - dt);
        });
    }
}

/// <summary>Rebuilds the enemy spatial grid from current positions.</summary>
public sealed class EnemyGridSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Enemy, Position, Radius, Elevation>();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        grid.Clear();
        World.Query(in Query, (Entity entity, ref Position position, ref Radius radius, ref Elevation elevation) =>
            grid.Add(entity, position.Value, radius.Value, elevation.Height));
        grid.Build();
    }
}

/// <summary>Removes dead enemies and drops XP gems where they fell.</summary>
public sealed class EnemyDeathSystem(World world, GameState state) : GameSystem(world, state)
{
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Enemy, Health, Position>();
    private readonly List<(Vector2 Position, int Xp)> _drops = new();

    public override void Update(float dt)
    {
        _drops.Clear();
        World.Query(in Query, (Entity entity, ref Enemy enemy, ref Health health, ref Position position) =>
        {
            if (health.Current > 0f)
                return;
            State.Destroy(entity);
            State.Kills++;
            _drops.Add((position.Value, enemy.XpValue));
        });

        // Structural changes happen outside the query.
        foreach (var (position, xp) in _drops)
            World.Create(new Position(position), new Velocity(), new Radius(0.25f), new XpGem { Value = xp });
    }
}
