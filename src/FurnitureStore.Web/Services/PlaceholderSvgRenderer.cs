using System.Globalization;
using FurnitureStore.Application.Common.Utilities;

namespace FurnitureStore.Web.Services;

/// <summary>
/// Draws simple, copyright-free furniture illustrations used as product images in the MVP.
/// Every value inserted into the SVG is either a whitelisted shape or a validated 6-digit hex color.
/// </summary>
public static class PlaceholderSvgRenderer
{
    private const string Leg = "#5b3a24";
    private const string Ink = "#2b2521";

    public static string? Render(string shape, string? colorHex, string? backgroundHex)
    {
        if (!PlaceholderImages.Shapes.Contains(shape))
        {
            return null;
        }

        var c = "#" + (PlaceholderImages.IsValidHex(colorHex) ? colorHex : "b07d4f");
        var bg = "#" + (PlaceholderImages.IsValidHex(backgroundHex) ? backgroundHex : PlaceholderImages.Backgrounds.Beige);
        var d = Shade(c, 0.78);
        var l = Tint(c, 0.35);

        var body = shape switch
        {
            "sofa" => Sofa(c, d, l),
            "armchair" => Armchair(c, d, l),
            "coffee-table" => CoffeeTable(c, d, l),
            "tv-stand" => TvStand(c, d, l),
            "bed" => Bed(c, d, l),
            "wardrobe" => Wardrobe(c, d),
            "nightstand" => Nightstand(c, l),
            "vanity" => Vanity(c, d, l),
            "dining-table" => DiningTable(c, d),
            "chair" => Chair(c, d),
            "dining-set" => DiningSet(c, d),
            "cabinet" => Cabinet(c, d, l),
            "desk" => Desk(c, d, l),
            "office-chair" => OfficeChair(c, d, l),
            "bookshelf" => Bookshelf(c, d),
            "wall-shelf" => WallShelf(c, d),
            "mirror" => Mirror(c, d),
            _ => string.Empty
        };

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="800" height="600" role="img">
            <rect width="400" height="300" fill="{bg}"/>
            <rect y="250" width="400" height="50" fill="#000" opacity=".03"/>
            {body}
            </svg>
            """;
    }

    private static string Shadow(int cx = 200, int rx = 150) => $"""<ellipse cx="{cx}" cy="252" rx="{rx}" ry="9" fill="#000" opacity=".09"/>""";

    private static string Sofa(string c, string d, string l) => Shadow() + $"""
        <rect x="70" y="118" width="260" height="84" rx="28" fill="{c}"/>
        <rect x="58" y="168" width="284" height="54" rx="18" fill="{d}"/>
        <rect x="44" y="140" width="48" height="86" rx="22" fill="{d}"/>
        <rect x="308" y="140" width="48" height="86" rx="22" fill="{d}"/>
        <rect x="96" y="150" width="102" height="40" rx="14" fill="{l}"/>
        <rect x="202" y="150" width="102" height="40" rx="14" fill="{l}"/>
        <rect x="74" y="224" width="8" height="24" rx="2" fill="{Leg}"/>
        <rect x="318" y="224" width="8" height="24" rx="2" fill="{Leg}"/>
        """;

    private static string Armchair(string c, string d, string l) => Shadow(rx: 95) + $"""
        <path d="M136 200 V112 Q200 64 264 112 V200 Z" fill="{c}"/>
        <rect x="118" y="160" width="164" height="56" rx="18" fill="{d}"/>
        <rect x="104" y="138" width="38" height="84" rx="17" fill="{d}"/>
        <rect x="258" y="138" width="38" height="84" rx="17" fill="{d}"/>
        <rect x="146" y="150" width="108" height="36" rx="12" fill="{l}"/>
        <path d="M130 220 L122 250 M270 220 L278 250" stroke="{Leg}" stroke-width="7" stroke-linecap="round"/>
        """;

    private static string CoffeeTable(string c, string d, string l) => Shadow(rx: 140) + $"""
        <rect x="112" y="160" width="10" height="86" rx="3" fill="{d}"/>
        <rect x="278" y="160" width="10" height="86" rx="3" fill="{d}"/>
        <rect x="195" y="172" width="10" height="72" rx="3" fill="{d}" opacity=".8"/>
        <ellipse cx="200" cy="154" rx="132" ry="34" fill="{d}"/>
        <ellipse cx="200" cy="146" rx="132" ry="34" fill="{c}"/>
        <ellipse cx="182" cy="140" rx="92" ry="20" fill="{l}" opacity=".35"/>
        """;

    private static string TvStand(string c, string d, string l) => Shadow() + $"""
        <rect x="112" y="58" width="176" height="86" rx="6" fill="{Ink}"/>
        <rect x="120" y="66" width="160" height="70" rx="3" fill="#3d3631"/>
        <rect x="192" y="144" width="16" height="6" fill="{Ink}"/>
        <rect x="50" y="150" width="300" height="80" rx="8" fill="{c}"/>
        <rect x="62" y="162" width="88" height="56" rx="4" fill="{d}"/>
        <rect x="250" y="162" width="88" height="56" rx="4" fill="{d}"/>
        <rect x="160" y="162" width="80" height="26" rx="3" fill="{l}"/>
        <rect x="160" y="192" width="80" height="26" rx="3" fill="{l}"/>
        <rect x="190" y="173" width="20" height="4" rx="2" fill="{Leg}"/>
        <rect x="190" y="203" width="20" height="4" rx="2" fill="{Leg}"/>
        <rect x="66" y="230" width="8" height="18" fill="{Leg}"/>
        <rect x="326" y="230" width="8" height="18" fill="{Leg}"/>
        <rect x="300" y="120" width="18" height="30" rx="6" fill="#e3d5c1"/>
        <ellipse cx="309" cy="106" rx="8" ry="18" fill="#7f9270"/>
        """;

    private static string Bed(string c, string d, string l) => Shadow(rx: 165) + $"""
        <rect x="36" y="86" width="30" height="154" rx="10" fill="{c}"/>
        <rect x="36" y="172" width="326" height="50" rx="10" fill="{c}"/>
        <rect x="60" y="150" width="298" height="32" rx="12" fill="#fbf8f3"/>
        <rect x="74" y="124" width="72" height="32" rx="12" fill="#ffffff" stroke="#e3d5c1" stroke-width="2"/>
        <path d="M168 150 H358 V178 H168 Z" fill="{l}"/>
        <rect x="350" y="150" width="14" height="72" rx="6" fill="{d}"/>
        <rect x="50" y="222" width="10" height="26" fill="{Leg}"/>
        <rect x="342" y="222" width="10" height="26" fill="{Leg}"/>
        """;

    private static string Wardrobe(string c, string d) => Shadow(rx: 110) + $"""
        <rect x="104" y="24" width="192" height="12" rx="3" fill="{d}"/>
        <rect x="110" y="32" width="180" height="208" rx="6" fill="{c}"/>
        <line x1="170" y1="38" x2="170" y2="234" stroke="{d}" stroke-width="3"/>
        <line x1="230" y1="38" x2="230" y2="234" stroke="{d}" stroke-width="3"/>
        <rect x="160" y="122" width="4" height="30" rx="2" fill="{Leg}"/>
        <rect x="176" y="122" width="4" height="30" rx="2" fill="{Leg}"/>
        <rect x="236" y="122" width="4" height="30" rx="2" fill="{Leg}"/>
        <rect x="116" y="238" width="168" height="10" fill="{d}"/>
        """;

    private static string Nightstand(string c, string l) => Shadow(rx: 90) + $"""
        <rect x="196" y="92" width="8" height="40" fill="{Ink}"/>
        <path d="M168 96 H232 L220 60 H180 Z" fill="#f6ead8" stroke="#d6c3aa" stroke-width="2"/>
        <rect x="130" y="130" width="140" height="100" rx="8" fill="{c}"/>
        <rect x="142" y="142" width="116" height="36" rx="4" fill="{l}"/>
        <rect x="142" y="184" width="116" height="36" rx="4" fill="{l}"/>
        <rect x="188" y="158" width="24" height="4" rx="2" fill="{Leg}"/>
        <rect x="188" y="200" width="24" height="4" rx="2" fill="{Leg}"/>
        <rect x="140" y="230" width="8" height="18" fill="{Leg}"/>
        <rect x="252" y="230" width="8" height="18" fill="{Leg}"/>
        """;

    private static string Vanity(string c, string d, string l) => Shadow(rx: 120) + $"""
        <circle cx="200" cy="84" r="52" fill="#e9f0f3" stroke="{c}" stroke-width="9"/>
        <path d="M176 70 L206 44" stroke="#ffffff" stroke-width="6" stroke-linecap="round" opacity=".7"/>
        <rect x="98" y="140" width="204" height="16" rx="4" fill="{c}"/>
        <rect x="236" y="156" width="58" height="46" rx="4" fill="{d}"/>
        <rect x="249" y="176" width="32" height="4" rx="2" fill="{l}"/>
        <rect x="106" y="156" width="8" height="92" fill="{d}"/>
        <rect x="286" y="202" width="8" height="46" fill="{d}"/>
        <rect x="160" y="196" width="62" height="16" rx="8" fill="{l}"/>
        <rect x="187" y="212" width="8" height="36" fill="{Leg}"/>
        """;

    private static string DiningTable(string c, string d) => Shadow(rx: 170) + $"""
        <rect x="96" y="160" width="10" height="72" fill="{d}" opacity=".7"/>
        <rect x="294" y="160" width="10" height="72" fill="{d}" opacity=".7"/>
        <path d="M70 128 H330 L362 158 H38 Z" fill="{c}"/>
        <rect x="38" y="158" width="324" height="11" fill="{d}"/>
        <rect x="56" y="169" width="12" height="80" fill="{d}"/>
        <rect x="332" y="169" width="12" height="80" fill="{d}"/>
        """;

    private static string Chair(string c, string d) => Shadow(rx: 75) + $"""
        <path d="M148 72 Q200 50 252 72 V96 Q200 80 148 96 Z" fill="{c}"/>
        <rect x="154" y="92" width="10" height="76" fill="{c}"/>
        <rect x="236" y="92" width="10" height="76" fill="{c}"/>
        <rect x="190" y="98" width="20" height="60" rx="4" fill="{c}" opacity=".85"/>
        <rect x="138" y="160" width="124" height="22" rx="8" fill="{d}"/>
        <rect x="146" y="182" width="10" height="66" fill="{Leg}"/>
        <rect x="244" y="182" width="10" height="66" fill="{Leg}"/>
        """;

    private static string DiningSet(string c, string d) => Shadow(rx: 175) + $"""
        <rect x="40" y="98" width="12" height="92" rx="4" fill="{c}"/>
        <rect x="40" y="170" width="62" height="14" rx="5" fill="{d}"/>
        <rect x="44" y="184" width="7" height="64" fill="{Leg}"/>
        <rect x="92" y="184" width="7" height="64" fill="{Leg}"/>
        <rect x="348" y="98" width="12" height="92" rx="4" fill="{c}"/>
        <rect x="298" y="170" width="62" height="14" rx="5" fill="{d}"/>
        <rect x="349" y="184" width="7" height="64" fill="{Leg}"/>
        <rect x="301" y="184" width="7" height="64" fill="{Leg}"/>
        <path d="M112 138 H288 L310 158 H90 Z" fill="{c}"/>
        <rect x="90" y="158" width="220" height="9" fill="{d}"/>
        <rect x="104" y="167" width="10" height="82" fill="{d}"/>
        <rect x="286" y="167" width="10" height="82" fill="{d}"/>
        """;

    private static string Cabinet(string c, string d, string l) => Shadow(rx: 100) + $"""
        <rect x="120" y="58" width="160" height="182" rx="6" fill="{c}"/>
        <rect x="132" y="70" width="64" height="102" rx="3" fill="#dfe8ec" opacity=".9"/>
        <rect x="204" y="70" width="64" height="102" rx="3" fill="#dfe8ec" opacity=".9"/>
        <line x1="132" y1="120" x2="196" y2="120" stroke="{d}" stroke-width="3"/>
        <line x1="204" y1="120" x2="268" y2="120" stroke="{d}" stroke-width="3"/>
        <rect x="146" y="92" width="8" height="26" rx="3" fill="#6b4a5a" opacity=".7"/>
        <rect x="162" y="96" width="8" height="22" rx="3" fill="#6b7f59" opacity=".7"/>
        <rect x="220" y="140" width="10" height="30" rx="4" fill="#c27c6b" opacity=".7"/>
        <rect x="132" y="180" width="136" height="50" rx="3" fill="{d}"/>
        <rect x="186" y="202" width="28" height="4" rx="2" fill="{l}"/>
        <rect x="128" y="240" width="8" height="10" fill="{Leg}"/>
        <rect x="264" y="240" width="8" height="10" fill="{Leg}"/>
        """;

    private static string Desk(string c, string d, string l) => Shadow(rx: 150) + $"""
        <path d="M132 138 L142 96 H214 L206 138 Z" fill="#3b3b3b"/>
        <rect x="122" y="134" width="98" height="6" rx="2" fill="#6b6b6b"/>
        <rect x="262" y="112" width="6" height="28" fill="{Ink}"/>
        <path d="M250 114 H280 L272 92 H258 Z" fill="#f6ead8"/>
        <rect x="68" y="140" width="264" height="14" rx="4" fill="{c}"/>
        <rect x="230" y="154" width="92" height="52" rx="4" fill="{d}"/>
        <rect x="252" y="170" width="48" height="4" rx="2" fill="{l}"/>
        <rect x="252" y="188" width="48" height="4" rx="2" fill="{l}"/>
        <rect x="78" y="154" width="10" height="94" fill="{Leg}"/>
        <rect x="310" y="206" width="10" height="42" fill="{Leg}"/>
        """;

    private static string OfficeChair(string c, string d, string l) => Shadow(rx: 80) + $"""
        <rect x="150" y="38" width="100" height="112" rx="32" fill="{c}"/>
        <rect x="162" y="50" width="76" height="88" rx="24" fill="{l}" opacity=".35"/>
        <rect x="126" y="130" width="10" height="38" rx="4" fill="#3b3b3b"/>
        <rect x="264" y="130" width="10" height="38" rx="4" fill="#3b3b3b"/>
        <rect x="134" y="150" width="132" height="30" rx="14" fill="{d}"/>
        <rect x="195" y="180" width="10" height="40" fill="#555555"/>
        <path d="M200 222 L140 238 M200 222 L260 238 M200 222 L168 246 M200 222 L232 246" stroke="#3b3b3b" stroke-width="8" stroke-linecap="round"/>
        <circle cx="138" cy="243" r="6" fill="#222222"/>
        <circle cx="262" cy="243" r="6" fill="#222222"/>
        <circle cx="166" cy="249" r="5" fill="#222222"/>
        <circle cx="234" cy="249" r="5" fill="#222222"/>
        """;

    private static string Bookshelf(string c, string d)
    {
        var shelves = string.Concat(new[] { 80, 120, 160, 200 }.Select(y =>
            $"""<rect x="136" y="{y}" width="128" height="6" fill="{c}"/>"""));
        const string books = """
            <rect x="146" y="50" width="10" height="30" fill="#c27c6b"/><rect x="158" y="56" width="8" height="24" fill="#6b7f59"/><rect x="168" y="52" width="12" height="28" fill="#e3d5c1"/>
            <rect x="214" y="92" width="10" height="28" fill="#26354f"/><rect x="226" y="96" width="8" height="24" fill="#c99a2e"/>
            <rect x="150" y="130" width="12" height="30" fill="#e3d5c1"/><rect x="164" y="136" width="10" height="24" fill="#c27c6b"/><rect x="176" y="132" width="8" height="28" fill="#6b7f59"/>
            <circle cx="236" cy="186" r="12" fill="#7f9270"/><rect x="228" y="190" width="16" height="10" fill="#e3d5c1"/>
            <rect x="152" y="212" width="12" height="28" fill="#26354f"/><rect x="166" y="216" width="10" height="24" fill="#c99a2e"/>
            """;
        return Shadow(rx: 95) + $"""
            <rect x="128" y="30" width="144" height="216" rx="4" fill="{c}"/>
            <rect x="138" y="40" width="124" height="198" fill="{d}" opacity=".45"/>
            {shelves}
            {books}
            """;
    }

    private static string WallShelf(string c, string d) => $"""
        <rect x="100" y="90" width="200" height="10" rx="2" fill="{c}"/>
        <rect x="104" y="100" width="192" height="6" fill="#000" opacity=".06"/>
        <rect x="124" y="160" width="200" height="10" rx="2" fill="{c}"/>
        <rect x="128" y="170" width="192" height="6" fill="#000" opacity=".06"/>
        <rect x="88" y="230" width="200" height="10" rx="2" fill="{c}"/>
        <rect x="92" y="240" width="192" height="6" fill="#000" opacity=".06"/>
        <rect x="126" y="66" width="22" height="24" rx="4" fill="#e3d5c1"/><ellipse cx="137" cy="54" rx="12" ry="16" fill="#7f9270"/>
        <rect x="230" y="62" width="10" height="28" fill="#c27c6b"/><rect x="242" y="68" width="8" height="22" fill="#26354f"/><rect x="252" y="64" width="12" height="26" fill="{d}"/>
        <rect x="150" y="140" width="40" height="20" rx="3" fill="#e3d5c1"/><rect x="270" y="134" width="18" height="26" rx="8" fill="#c99a2e" opacity=".8"/>
        <rect x="120" y="206" width="12" height="24" fill="#6b7f59"/><rect x="134" y="210" width="10" height="20" fill="#e3d5c1"/><circle cx="240" cy="216" r="14" fill="#7f9270"/><rect x="230" y="220" width="20" height="10" fill="#e3d5c1"/>
        """;

    private static string Mirror(string c, string d) => Shadow(rx: 70) + $"""
        <path d="M244 150 L282 250" stroke="{d}" stroke-width="8" stroke-linecap="round"/>
        <path d="M148 250 V110 A52 52 0 0 1 252 110 V250 Z" fill="{c}"/>
        <path d="M160 240 V112 A40 40 0 0 1 240 112 V240 Z" fill="#e6eef2"/>
        <path d="M178 128 L214 92" stroke="#ffffff" stroke-width="7" stroke-linecap="round" opacity=".7"/>
        <path d="M186 160 L226 120" stroke="#ffffff" stroke-width="4" stroke-linecap="round" opacity=".5"/>
        """;

    // ------------------------------------------------------------------ color helpers

    private static (int R, int G, int B) Parse(string hex) =>
        (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));

    private static string ToHex(double r, double g, double b) =>
        $"#{Clamp(r):x2}{Clamp(g):x2}{Clamp(b):x2}";

    private static int Clamp(double value) => (int)Math.Round(Math.Clamp(value, 0, 255));

    internal static string Shade(string hex, double factor)
    {
        var (r, g, b) = Parse(hex);
        return ToHex(r * factor, g * factor, b * factor);
    }

    internal static string Tint(string hex, double amount)
    {
        var (r, g, b) = Parse(hex);
        return ToHex(r + (255 - r) * amount, g + (255 - g) * amount, b + (255 - b) * amount);
    }
}
