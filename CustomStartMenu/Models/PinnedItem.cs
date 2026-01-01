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
    public int Order { get; set; }
    
    /// <summary>
    /// The tab this item belongs to (null = default/first tab)
    /// </summary>
    public string? TabId { get; set; }
    
    /// <summary>
    /// The group this item belongs to within its tab (null = ungrouped)
    /// </summary>
    public string? GroupId { get; set; }
}

public enum PinnedItemType
{
    Application,
    Folder
}
