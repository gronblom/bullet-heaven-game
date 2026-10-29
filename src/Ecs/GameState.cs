using System;
using System.Collections.Generic;
using Arch.Core;
using Godot;

namespace BulletHeaven.Ecs;

/// <summary>Run-wide data shared by systems that doesn't belong to a single entity.</summary>
public sealed class GameState
{
    public Entity Player;
    public readonly Rect2 Bounds;
    public readonly SpatialGrid EnemyGrid;
    /// <summary>Static level geometry; set by Main once the level has been built.</summary>
    public Terrain Terrain;
    public readonly Random Rng = new();

    public float Time;
    public int Kills;
    public int Level = 1;
    public int Xp;
    public int XpToNext = 5;
    public bool GameOver;

    // Player-wide modifiers changed by upgrades.
    public float MagnetRadius = 4f;
    public float RamDamagePerSpeed = 2.5f;
    public float StompDamage = 30f;

    public string Message = "";
    public float MessageTimer;

    private readonly HashSet<Entity> _toDestroy = new();

    public GameState(Rect2 bounds)
    {
        Bounds = bounds;
        EnemyGrid = new SpatialGrid(bounds.Grow(2f), 1f);
    }

    /// <summary>Queue an entity for destruction at the end of the frame (safe to call inside queries).</summary>
    public void Destroy(Entity entity) => _toDestroy.Add(entity);

    public void FlushDestroyed(World world)
    {
        foreach (var entity in _toDestroy)
        {
            if (world.IsAlive(entity))
                world.Destroy(entity);
        }
        _toDestroy.Clear();
    }

    public void ShowMessage(string text, float seconds = 2f)
    {
        Message = text;
        MessageTimer = seconds;
    }
}
