using System.Windows.Input;
using System.Windows.Interop;

namespace CustomStartMenu.Models;

/// <summary>
/// Application settings for customizing Start Menu behavior and appearance
/// </summary>
public class AppSettings
{
    /// <summary>
    /// When true, pinned items display only icons without text labels
    /// </summary>
    public bool ShowIconsOnly { get; set; } = false;

    /// <summary>
    /// Menu background transparency (0.0 = fully transparent, 1.0 = fully opaque)
    /// Default is 0.85 (85% opaque)
    /// </summary>
    public double MenuTransparency { get; set; } = 0.85;

    /// <summary>
    /// Layout mode for pinned items (Ordered = auto-grid, FreeForm = user-positioned)
    /// </summary>
    public LayoutMode PinnedItemsLayout { get; set; } = LayoutMode.Ordered;

    /// <summary>
    /// URL template for web searches. The search query will be appended to this URL.
    /// </summary>
    public string WebSearchUrl { get; set; } = "https://www.google.com/search?q=";

    /// <summary>
    /// Enable menu open/close animations
    /// </summary>
    public bool EnableAnimations { get; set; } = true;

    /// <summary>
    /// Menu position on screen (Left or Center)
    /// </summary>
    public MenuPosition Position { get; set; } = MenuPosition.Left;

    /// <summary>
    /// Hotkey configuration for opening the menu
    /// </summary>
    public HotkeyConfig OpenMenuHotkey { get; set; } = new();

    /// <summary>
    /// Override Windows Start button click to open this menu
    /// </summary>
    public bool OverrideWindowsStartButton { get; set; } = true;

    /// <summary>
    /// Item size in pixels for the menu grid (default: 80)
    /// This determines both item button size and grid cell size
    /// </summary>
    public int ItemSize { get; set; } = 80;

    /// <summary>
    /// Menu size preset
    /// </summary>
    public MenuSize Size { get; set; } = MenuSize.Normal;

    /// <summary>
    /// Custom width when Size is Custom
    /// </summary>
    public int CustomWidth { get; set; } = 650;

    /// <summary>
    /// Custom height when Size is Custom
    /// </summary>
    public int CustomHeight { get; set; } = 750;

    /// <summary>
    /// Theme accent color (hex string)
    /// </summary>
    public string AccentColor { get; set; } = "#0078D4";

    /// <summary>
    /// Background darkness level (0 = black, 255 = light gray)
    /// Default is 32 (#202020)
    /// </summary>
    public int BackgroundDarkness { get; set; } = 32;
}

/// <summary>
/// Hotkey configuration with modifier keys
/// </summary>
public class HotkeyConfig
{
    public bool UseWinKey { get; set; } = true;
    public bool Ctrl { get; set; } = false;
    public bool Alt { get; set; } = false;
    public bool Shift { get; set; } = false;
    public int KeyCode { get; set; } = 0; // 0 = no key (just Win key)

    public override string ToString()
    {
        var parts = new List<string>();
        if (UseWinKey) parts.Add("Win");
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (KeyCode > 0)
        {
            // Convert virtual key code to Key enum properly
            var key = KeyInterop.KeyFromVirtualKey(KeyCode);
            parts.Add(key.ToString());
        }
        return parts.Count > 0 ? string.Join(" + ", parts) : "Win";
    }
}

/// <summary>
/// Layout mode for pinned items arrangement
/// </summary>
public enum LayoutMode
{
    /// <summary>
    /// Items are automatically compacted - no gaps between items
    /// </summary>
    Ordered,

    /// <summary>
    /// Items stay at their placed grid positions - gaps allowed
    /// </summary>
    FreeForm
}

/// <summary>
/// Menu position on screen
/// </summary>
public enum MenuPosition
{
    Left,
    Center
}

/// <summary>
/// Menu size presets
/// </summary>
public enum MenuSize
{
    Small,      // 500x600
    Normal,     // 650x750
    Large,      // 900x850
    VeryLarge,  // 1100x950
    Fullscreen, // Screen dimensions
    Custom      // User-defined
}
