using System;
using System.Collections.Generic;
using Arch.Core;
using BulletHeaven.Ecs;
using Godot;

namespace BulletHeaven.Systems;

/// <summary>Spilled liquids: how sticky each one is, and how to spill one.</summary>
public static class Liquids
{
    public static float Slow(LiquidKind kind) => kind switch
    {
        LiquidKind.Water => 0.7f,
        LiquidKind.Coffee => 0.6f,
        LiquidKind.Juice => 0.5f,
        LiquidKind.Honey => 0.3f,
        _ => 0.35f, // jam
    };

    public static Color Color(LiquidKind kind) => kind switch
    {
        LiquidKind.Water => new Color(0.1f, 0.12f, 0.14f),
        LiquidKind.Coffee => new Color(0.12f, 0.05f, 0.02f),
        LiquidKind.Juice => new Color(0.85f, 0.38f, 0.03f),
        LiquidKind.Honey => new Color(0.7f, 0.4f, 0.04f),
        _ => new Color(0.3f, 0.01f, 0.1f),
    };

    /// <summary>Creates a puddle. <paramref name="lifetime"/> &lt; 0 keeps it for good.</summary>
    public static Entity Spill(World world, Vector2 at, float height, float radius, LiquidKind kind, float lifetime = -1f, float seed = 0f) =>
        world.Create(new Position(at), new Puddle
        {
            Kind = kind,
            Radius = radius,
            Height = height,
            Slow = Slow(kind),
            Lifetime = lifetime,
            Seed = seed,
        });
}

/// <summary>
/// Slows anything wading through a puddle at the puddle's height: bugs by scaling their velocity
/// this frame (after steering, before movement), the player through <see cref="GameState.PlayerSlow"/>
/// which caps their speed. Temporary puddles (jam from the mortar) dry up.
/// </summary>
public sealed class LiquidSystem(World world, GameState state) : GameSystem(world, state)
{
    private const float HeightTolerance = 0.6f;
    private static readonly QueryDescription Query = new QueryDescription().WithAll<Puddle, Position>();
    private readonly List<int> _inside = new();

    public override void Update(float dt)
    {
        var grid = State.EnemyGrid;
        var playerPos = World.Get<Position>(State.Player).Value;
        float playerHeight = World.Get<Elevation>(State.Player).Height;
        float playerSlow = 1f;

        World.Query(in Query, (Entity entity, ref Puddle puddle, ref Position position) =>
        {
            puddle.Age += dt;
            if (puddle.Lifetime >= 0f && puddle.Age > puddle.Lifetime)
            {
                State.Destroy(entity);
                return;
            }

            if (MathF.Abs(playerHeight - puddle.Height) < HeightTolerance && playerPos.DistanceTo(position.Value) < puddle.Radius)
                playerSlow = MathF.Min(playerSlow, puddle.Slow);

            _inside.Clear();
            grid.Query(position.Value, puddle.Radius, _inside);
            foreach (int i in _inside)
            {
                if (MathF.Abs(grid.Heights[i] - puddle.Height) > HeightTolerance || grid.Positions[i].DistanceTo(position.Value) > puddle.Radius)
                    continue;
                var bug = grid.Entities[i];
                if (World.IsAlive(bug))
                    World.Get<Velocity>(bug).Value *= puddle.Slow;
            }
        });

        State.PlayerSlow = playerSlow;
        if (World.TryGet<Car>(State.Player, out _) && World.Has<CarStats>(State.Player))
        {
            // The car: cap its speed while in the puddle.
            ref var velocity = ref World.Get<Velocity>(State.Player);
            float cap = World.Get<CarStats>(State.Player).MaxSpeed * playerSlow;
            if (velocity.Value.Length() > cap)
                velocity.Value = velocity.Value.Normalized() * cap;
        }
    }
}
