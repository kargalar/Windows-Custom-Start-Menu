using System.Text.Json.Serialization;

namespace CustomStartMenu.Models;

/// <summary>
/// Represents a pinned item (application or folder)
/// </summary>
public class PinnedItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? IconPath { get; set; }
    public PinnedItemType Type { get; set; }
    public DateTime PinnedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// The tab this item belongs to (null = default/first tab)
    /// </summary>
    public string? TabId { get; set; }
    
    /// <summary>
    /// The group this item belongs to within its tab (null = ungrouped)
    /// </summary>
    public string? GroupId { get; set; }
    
    /// <summary>
    /// Custom display name set by user (independent of file name)
    /// </summary>
    public string? CustomName { get; set; }
    
    /// <summary>
    /// Grid row position (0-based)
    /// </summary>
    public int GridRow { get; set; }
    
    /// <summary>
    /// Grid column position (0-based)
    /// </summary>
    public int GridColumn { get; set; }
    
    /// <summary>
    /// Display name to show in UI - uses CustomName if set, otherwise falls back to Name
    /// </summary>
    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(CustomName) ? CustomName : Name;
}

public enum PinnedItemType
{
    Application,
    Folder,
    InternetShortcut
}
