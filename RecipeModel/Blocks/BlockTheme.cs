namespace APS.RecipeModel.Blocks;

public static class BlockTheme
{
    public static Color HardwareCategory { get; } = Color.FromArgb(32, 120, 128);
    public static Color PowerCategory    { get; } = Color.FromArgb(196, 140, 50);
    public static Color ProcessCategory  { get; } = Color.FromArgb(130, 80, 180);
    public static Color TimingCategory   { get; } = Color.FromArgb(64, 120, 200);
    public static Color FlowCategory     { get; } = Color.FromArgb(214, 120, 50);
    public static Color SubroutineCategory { get; } = Color.FromArgb(70, 160, 90);
    public static Color StructureCategory { get; } = Color.FromArgb(120, 130, 150);

    public static Color BlockBodyBg     { get; } = Color.FromArgb(22, 32, 50);
    public static Color BlockBorder     { get; } = Color.FromArgb(90, 130, 180);
    // Selection ring — warm amber that reads as "selected" against every category
    // color AND the dark canvas. Deliberately NOT a brightened category shade (blends
    // into the block's own header) and not near-white (lost against light headers).
    public static Color SelectionRing   { get; } = Color.FromArgb(255, 255, 196, 64);
    // Outer band reserved on every block for the selection ring (3 px amber + 1 px gap).
    public static int SelectionRingWidth => Scale(4);
    // Dark hairline between the ring and the block so the ring stays visible over
    // header colours close to amber (POWER orange).
    public static Color SelectionGap    { get; } = Color.FromArgb(255, 13, 27, 42);
    public static Color BlockTextOnCat  { get; } = Color.FromArgb(230, 240, 250);
    public static Color LabelForeground { get; } = Color.FromArgb(170, 184, 200);

    // Layout pixels are 96-DPI logical values scaled once by the system DPI.
    // Every host runs ApplicationHighDpiMode=SystemAware, so the factor is
    // fixed for the process lifetime (no per-monitor rescale to track).
    // Point-sized fonts already scale with DPI; only raw pixels need this.
    private static readonly float DpiFactor = ReadSystemDpiFactor();

    private static float ReadSystemDpiFactor()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiY / 96f;
    }

    public static int Scale(int px) => (int)Math.Round(px * DpiFactor);

    public static readonly int HeaderHeight = Scale(22);
    public static readonly int GutterWidth = Scale(10);
    public static readonly int BlockPadding = Scale(2);
    public static readonly int MouthIndent = Scale(16);
    public static readonly int BlockSpacing = Scale(8);
    public static readonly int HeaderColumnWidth = Scale(80);
    public const int CornerRadius = 0;
}
