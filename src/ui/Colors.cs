using Godot;

namespace PredictEverything;

/// <summary>
/// Centralised colour tokens for the PredictEverything UI.
/// One semantic value = one colour — every Godot Color and its
/// matching BBCode hex live here so nothing drifts out of sync.
/// </summary>
public static class Colors
{
    // ── Background & border ──────────────────────────────────
    public static readonly Color BgPrimary     = new(0.043f, 0.055f, 0.102f, 0.92f);
    public static readonly Color BgSecondary   = new(0.067f, 0.078f, 0.129f, 0.98f);
    public static readonly Color BorderPrimary = new(0.110f, 0.133f, 0.192f, 1f);

    // ── Text hierarchy ───────────────────────────────────────
    public static readonly Color TextPrimary   = new(0.800f, 0.784f, 0.718f);
    public static readonly Color TextSecondary = new(0.545f, 0.525f, 0.475f);
    public static readonly Color TextDim       = new(0.400f, 0.384f, 0.349f);

    // ── Column identity (reward-type accents) ────────────────
    public static readonly Color RareAccent     = new(0.929f, 0.471f, 0.188f);  // #ED7830  Amber Flame
    public static readonly Color UncommonAccent = new(0.357f, 0.608f, 0.882f);  // #5B9BE1  Arcane Blue
    public static readonly Color CommonAccent   = new(0.588f, 0.627f, 0.682f);  // #96A0AE  Moon Silver
    public static readonly Color RelicAccent    = new(0.243f, 0.796f, 0.541f);  // #3ECB8A  Emerald
    public static readonly Color PotionAccent   = new(0.612f, 0.471f, 0.859f);  // #9C78DB  Amethyst

    // ── State colours ────────────────────────────────────────
    public static readonly Color LockedColor    = new(0.180f, 0.769f, 0.714f);  // #2EC4B6  Jade
    public static readonly Color PlannedColor   = new(0.910f, 0.659f, 0.200f);  // #E8A833  Amber Gold
    public static readonly Color UpgradedColor  = new(0.627f, 0.839f, 0.212f);  // #A0D636  Vital Green
    public static readonly Color ErrorColor     = new(0.878f, 0.251f, 0.251f);  // #E04040  True Red
    public static readonly Color CurseColor     = new(0.878f, 0.251f, 0.251f);  // #E04040  same as error
    public static readonly Color WarningColor   = new(0.910f, 0.659f, 0.200f);  // #E8A833  same as planned
    public static readonly Color SuccessColor   = new(0.180f, 0.769f, 0.714f);  // #2EC4B6  same as locked

    // ── BBCode hex strings (mechanically derived from above) ──
    public const string HexRare       = "#ED7830";
    public const string HexUncommon   = "#5B9BE1";
    public const string HexCommon     = "#96A0AE";
    public const string HexRelic      = "#3ECB8A";
    public const string HexPotion     = "#9C78DB";
    public const string HexLocked     = "#2EC4B6";
    public const string HexPlanned    = "#E8A833";
    public const string HexUpgraded   = "#A0D636";
    public const string HexError      = "#E04040";
    public const string HexSuccess    = "#2EC4B6";
    public const string HexNormal     = "#CCC8B7";
}
