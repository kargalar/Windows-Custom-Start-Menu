namespace CustomStartMenu.Models;

/// <summary>
/// Represents a group that can contain pinned items within a tab
/// </summary>
public class Group
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Yeni Grup";
    public string? TabId { get; set; } // Which tab this group belongs to (null = default tab)
    public int Order { get; set; }
    
    /// <summary>
    /// Global order for mixed sorting with ungrouped items in ordered layout
    /// </summary>
    public int GlobalOrder { get; set; }
    
    /// <summary>
    /// Grid row position for free-form layout mode (null = auto-positioned)
    /// </summary>
    public int? GridRow { get; set; }
    
    /// <summary>
    /// Grid column position for free-form layout mode (null = auto-positioned)
    /// </summary>
    public int? GridColumn { get; set; }
    
    public bool IsExpanded { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
