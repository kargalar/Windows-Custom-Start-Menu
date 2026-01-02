namespace CustomStartMenu.Models;

/// <summary>
/// Represents a group (folder) that can contain pinned items within a tab
/// </summary>
public class Group
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Yeni Klasör";
    
    /// <summary>
    /// Which tab this group belongs to (null = default tab)
    /// </summary>
    public string? TabId { get; set; }
    
    /// <summary>
    /// Grid row position (0-based)
    /// </summary>
    public int GridRow { get; set; }
    
    /// <summary>
    /// Grid column position (0-based)
    /// </summary>
    public int GridColumn { get; set; }
    
    public bool IsExpanded { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
