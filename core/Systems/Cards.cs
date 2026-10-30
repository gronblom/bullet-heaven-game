using System.Collections.Generic;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Systems;

/// <summary>
/// One card in a menu (loadout, level-up, pause), as both front-ends draw it: a title, a small
/// category line, a description, an icon (assets/icons/{Icon}.png), an accent colour and, for
/// modifiers, how many times it has been picked out of how many.
/// </summary>
public sealed record MenuCard(string Title, string Category, string Text, string Icon, Color Accent, int Picks = 0, int MaxPicks = 0);

/// <summary>The cards on offer: the run-start loadout and level-up modifiers.</summary>
public static class Cards
{
    /// <summary>Every weapon is on offer at the start; pick <see cref="LoadoutPicks"/> of them.</summary>
    public static readonly WeaponKind[] Loadout = { WeaponKind.Minigun, WeaponKind.LightningWand, WeaponKind.SeedMortar, WeaponKind.BugSpray };
    public const int LoadoutPicks = 2;

    public static Color Accent(WeaponKind kind) => kind switch
    {
        WeaponKind.Minigun => new Color(1f, 0.55f, 0.15f),
        WeaponKind.LightningWand => new Color(0.4f, 0.75f, 1f),
        WeaponKind.SeedMortar => new Color(0.55f, 0.8f, 0.25f),
        _ => new Color(0.85f, 0.45f, 0.95f),
    };

    public static string Icon(WeaponKind kind) => kind switch
    {
        WeaponKind.Minigun => "minigun",
        WeaponKind.LightningWand => "wand",
        WeaponKind.SeedMortar => "mortar",
        _ => "spray_can",
    };

    private static readonly Color PassiveAccent = new(1f, 0.8f, 0.35f);

    public static MenuCard Weapon(WeaponKind kind) =>
        new(WeaponStats.Name(kind), "WEAPON", WeaponStats.Blurb(kind), Icon(kind), Accent(kind));

    public static List<MenuCard> Weapons(IEnumerable<WeaponKind> kinds)
    {
        var cards = new List<MenuCard>();
        foreach (var kind in kinds)
            cards.Add(Weapon(kind));
        return cards;
    }

    public static MenuCard Modifier(GameState state, Modifier modifier) => modifier.Weapon is { } kind
        ? new(modifier.Name, WeaponStats.Name(kind).ToUpperInvariant(), modifier.Description, Icon(kind), Accent(kind),
            Modifiers.Picks(state, modifier), modifier.MaxPicks)
        : new(modifier.Name, "YOU", modifier.Description, modifier.Name == "Gem magnet" ? "gem" : "runner", PassiveAccent,
            Modifiers.Picks(state, modifier), modifier.MaxPicks);

    public static List<MenuCard> Pause() => new()
    {
        new("Resume", "", "Back to the bugs", "runner", new Color(0.5f, 0.85f, 0.5f)),
        new("Restart", "", "Start this level over", "toy_block", new Color(1f, 0.7f, 0.3f)),
        new("Kitchen", "", "Back to the first level", "mug_red", new Color(0.9f, 0.4f, 0.35f)),
    };
}
