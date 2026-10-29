using Arch.Core;
using BulletHeaven.Ecs;

namespace BulletHeaven.Systems;

/// <summary>A system runs once per frame over the ECS world. Order is defined in Main.</summary>
public abstract class GameSystem(World world, GameState state)
{
    protected readonly World World = world;
    protected readonly GameState State = state;

    public abstract void Update(float dt);
}
