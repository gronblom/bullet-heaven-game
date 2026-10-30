using System;
using System.Collections.Generic;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Levels;

/// <summary>
/// Level 1. A kitchen seen from a bug's-eye scale. The floor is the main arena; the kitchen counter
/// in the middle is high ground (10 up) with books, mugs, fruit and spills on it. Two ways up:
///   - west: a cutting board leaning on a stool, then a hop across to the counter;
///   - east: a staircase of toy blocks, one jump per block.
/// Anywhere is a way down.
///
/// The secret: books -> toaster (14.6) where the parachute lies. Glide from there across to the back
/// cabinets (unreachable otherwise) and into the open window to unlock the Windowsill.
/// </summary>
public sealed class Kitchen : Level
{
    private const float CounterTop = 10f;
    private const float StoolSeat = 9f;
    private static readonly Rect2 Counter = new(-26f, -18f, 52f, 26f);

    public override string Name => "Kitchen";
    public override Rect2 Bounds => new(-80f, -55f, 160f, 110f);
    public override Vector2 PlayerStart => new(0f, 30f);
    public override Rect2? Exit => new Rect2(-5f, -55f, 30f, 10f);
    public override float ExitHeight => CounterTop - 0.5f;
    public override int ExitTo => 2; // the Windowsill

    private static readonly Vector2 Toaster = new(8f, -16f);
    private const float ToasterHeight = 4.6f;

    protected override void BuildLevel()
    {
        Room();
        KitchenCounter();
        CounterProps();
        WaysUp();
        FloorProps();
    }

    private void Room()
    {
        Floor("floor", new Color(0.4f, 0.27f, 0.15f));
        RoomWalls(new Look(new Color(0.55f, 0.48f, 0.4f), 0.9f));
        // The window over the back cabinets, open at the bottom: the way out to the windowsill.
        Primitive(Shape.Box, new Vector3(10f, CounterTop, Bounds.Position.Y + 0.2f), new Vector3(30f, 18f, 0.3f),
            new Look(new Color(0.85f, 0.93f, 1f), Glow: 1.4f));

        // Fridge and a row of cabinets along the back wall.
        Block(new Vector2(-66f, -45f), new Vector2(24f, 20f), 0f, 45f, new Color(0.92f, 0.93f, 0.95f), "fridge");
        BoxObstacle(new Vector2(10f, -50f), new Vector2(120f, 10f), CounterTop);
        Model("back_counter", new Vector3(10f, 0f, -50f), new Vector3(121f, CounterTop, 11f), Shape.Box,
            new Vector3(120f, CounterTop, 10f), new Look(new Color(0.1f, 0.14f, 0.24f)));
    }

    private void KitchenCounter()
    {
        var center = Counter.GetCenter();
        BoxObstacle(center, Counter.Size, CounterTop);
        var size = new Vector3(Counter.Size.X + 1f, CounterTop, Counter.Size.Y + 1f);
        Model("counter", new Vector3(center.X, 0f, center.Y), size, Shape.Box, size, new Look(new Color(0.1f, 0.14f, 0.24f)),
            fade: Counter.Grow(0.5f), fadeTop: CounterTop);
    }

    private void CounterProps()
    {
        const float b = CounterTop;
        var rulerWood = new Color(0.85f, 0.7f, 0.45f);

        Round("mug_white", new Vector2(-18f, -12f), b, 3f, 6f, new Color(0.92f, 0.92f, 0.9f), sphere: false, fadeRadius: 4.6f);
        Round("mug_red", new Vector2(19f, 3f), b, 3f, 6f, new Color(0.8f, 0.2f, 0.2f), sphere: false, fadeRadius: 4.6f);
        Spill(new Vector2(17f, 6.5f), b, 2.4f, LiquidKind.Coffee); // dribbled from the red mug
        Spill(new Vector2(-5f, -5f), b, 2.2f, LiquidKind.Honey);

        // Two-level book stack with ruler ramps; fly off the far side, or hop on to the toaster.
        Block(new Vector2(6f, -10f), new Vector2(9f, 6.5f), b, 1.6f, new Color(0.2f, 0.35f, 0.7f), "book_blue");
        Block(new Vector2(8f, -10f), new Vector2(5f, 4f), b + 1.6f, 1.4f, new Color(0.75f, 0.6f, 0.2f), "book_gold");
        Ramp(new Vector2(-1.5f, -10f), Vector2.Right, 6f, 3f, b, 1.6f, rulerWood, plank: true, model: "ruler");
        Ramp(new Vector2(3.5f, -10f), Vector2.Right, 4f, 3f, b + 1.6f, 1.4f, rulerWood, plank: true, model: "ruler");

        // The toaster at the back edge of the counter, with the parachute lying on top.
        Block(Toaster, new Vector2(6f, 4f), b, ToasterHeight, new Color(0.8f, 0.8f, 0.82f), "toaster");
        Item(PickupKind.Parachute, Toaster, b + ToasterHeight);

        Block(new Vector2(22f, -13f), new Vector2(4f, 7f), b, 14f, new Color(0.95f, 0.75f, 0.15f), "cereal_box");
        Round("apple", new Vector2(-8f, 2f), b, 2.3f, 4.4f, new Color(0.75f, 0.1f, 0.08f), fadeRadius: 2.4f);

        // Pencil with an eraser kicker in front, and a launch ramp that throws you off the counter.
        Block(new Vector2(-14f, -3f), new Vector2(12f, 0.5f), b, 0.5f, new Color(0.95f, 0.8f, 0.1f), "pencil");
        Ramp(new Vector2(-14f, 1f), Vector2.Up, 4f, 3f, b, 1f, new Color(0.95f, 0.55f, 0.6f), model: "eraser");
        Ramp(new Vector2(19.5f, -3f), Vector2.Right, 5f, 3f, b, 1.4f, rulerWood, plank: true, model: "ruler");

        // Fruit (climbable): a strawberry and a pair of cherries.
        Round("strawberry", new Vector2(11f, 3f), b, 1.3f, 3.4f, new Color(0.8f, 0.05f, 0.05f));
        Round("cherries", new Vector2(-22f, 4f), b, 1.9f, 1.95f, new Color(0.4f, 0.02f, 0.04f));
    }

    private void WaysUp()
    {
        // West: stools pushed up to the counter (a short hop from seat to top), and a cutting
        // board leaning on one of them. Its top end meets the seat's edge, so that step is flush.
        Stool(new Vector2(-14f, 12f));
        Stool(new Vector2(14f, 12f));
        Ramp(new Vector2(-14f, 26f), Vector2.Up, 22f, 4f, 0f, StoolSeat, new Color(0.7f, 0.5f, 0.3f), plank: true);

        // East: toy block staircase, about one jump per block.
        var colors = new[]
        {
            new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.5f, 0.9f), new Color(0.95f, 0.8f, 0.15f),
            new Color(0.2f, 0.7f, 0.3f), new Color(0.6f, 0.3f, 0.8f), new Color(0.95f, 0.5f, 0.1f),
        };
        var tints = Array.ConvertAll(colors, c => c.Lightened(0.25f)); // paint over bare wood
        (Vector2 At, float Size, float Top)[] steps =
        {
            (new Vector2(36f, 14f), 5f, 1.6f),
            (new Vector2(36f, 8f), 5f, 3.2f),
            (new Vector2(36f, 2f), 5f, 4.8f),
            (new Vector2(36f, -4f), 5f, 6.4f),
            (new Vector2(36f, -10f), 5f, 8f),
            (new Vector2(29.5f, -10f), 5f, 9f), // bridges to the counter: a short hop either side
        };
        for (int i = 0; i < steps.Length; i++)
            Stack(steps[i].At, new Vector2(steps[i].Size, steps[i].Size), steps[i].Top, "toy_block", tints, i);
    }

    private void FloorProps()
    {
        // Dog bowl, and the water the dog splashed out of it.
        Round("dog_bowl", new Vector2(-45f, 30f), 0f, 4f, 1.5f, new Color(0.75f, 0.77f, 0.8f), sphere: false);
        Spill(new Vector2(-38f, 36f), 0f, 4.5f, LiquidKind.Water);
        Spill(new Vector2(-28f, -24f), 0f, 3.5f, LiquidKind.Juice); // squeezed out of the orange

        Round("ball", new Vector2(55f, 32f), 0f, 3.6f, 8f, new Color(0.2f, 0.6f, 0.95f), fadeRadius: 4f);
        Round("orange", new Vector2(-35f, -30f), 0f, 3.5f, 6.6f, new Color(0.95f, 0.45f, 0.05f));
        Block(new Vector2(25f, 40f), new Vector2(15f, 3.2f), 0f, 2.5f, new Color(0.95f, 0.8f, 0.1f), "banana");
    }

    // Treated as solid so nothing walks between the legs (the height map has no overhangs).
    private void Stool(Vector2 at) => Round("stool", at, 0f, 3f, StoolSeat, new Color(0.6f, 0.4f, 0.22f), sphere: false);
}
