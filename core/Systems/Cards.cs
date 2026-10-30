using System.Collections.Generic;
using BulletHeaven.Ecs;
using BulletHeaven.Numerics;

namespace BulletHeaven.Systems;

/// <summary>
/// One card in a menu (level-up, pause), as both front-ends draw it: a title, a small category line,
/// a description, an icon (assets/icons/{Icon}.png) and/or a big glyph (+4, x1.5, ~), an accent colour.
/// </summary>
public sealed record MenuCard(string Title, string Category, string Text, string Icon, Color Accent, string Glyph = null);

/// <summary>The cards on offer and the colours that identify weapons and module kinds.</summary>
public static class Cards
{
    public static Color Accent(WeaponKind kind) => kind switch
    {
        WeaponKind.ArcaneWand => new Color(0.4f, 0.75f, 1f),
        WeaponKind.Minigun => new Color(1f, 0.6f, 0.2f),
        WeaponKind.TdMortar => new Color(1f, 0.35f, 0.15f),
        _ => new Color(0.75f, 0.4f, 1f),
    };

    public static Color Accent(ModuleOp op) => op switch
    {
        ModuleOp.Add => new Color(0.45f, 0.9f, 0.45f),
        ModuleOp.Multiply => new Color(1f, 0.62f, 0.2f),
        ModuleOp.Geometry => new Color(0.78f, 0.5f, 1f),
        _ => new Color(1f, 0.82f, 0.35f),
    };

    public static string Icon(WeaponKind kind) => kind switch
    {
        WeaponKind.ArcaneWand => "wand",
        WeaponKind.Minigun => "minigun",
        WeaponKind.TdMortar => "mortar",
        _ => "void_pulser",
    };

    /// <summary>The module's arithmetic as a short symbol: +4, x1.5, or a sign for the geometry.</summary>
    public static string Glyph(Module m) => m.Op switch
    {
        ModuleOp.Add => $"+{m.Value:0.#}",
        ModuleOp.Multiply => $"×{m.Value:0.#}",
        ModuleOp.Geometry => m.Geometry switch
        {
            Geometry.Sine => "∿",
            Geometry.Spiral => "@",
            Geometry.Pentagon => "✦",
            Geometry.Radial => "✺",
            Geometry.Laser => "═",
            _ => "—",
        },
        _ => "♥",
    };

    public static string Category(Module m) => m.Op switch
    {
        ModuleOp.Player => "YOU",
        _ => $"{m.Op.ToString().ToUpperInvariant()} · {(m.Only is { } kind ? WeaponBase.Name(kind).ToUpperInvariant() : "ANY WEAPON")}",
    };

    public static MenuCard Module(Module m) =>
        new(m.Name, Category(m), m.Text, m.Only is { } kind ? Icon(kind) : null, Accent(m.Op), Glyph(m));

    public static List<MenuCard> Modules(List<Module> modules) => modules.ConvertAll(Module);

    public static List<MenuCard> Pause() => new()
    {
        new("Resume", "", "Back to the bugs", "runner", new Color(0.5f, 0.85f, 0.5f)),
        new("Restart", "", "Start this level over", "toy_block", new Color(1f, 0.7f, 0.3f)),
        new("Kitchen", "", "Back to the first level", "mug_red", new Color(0.9f, 0.4f, 0.35f)),
    };
}
