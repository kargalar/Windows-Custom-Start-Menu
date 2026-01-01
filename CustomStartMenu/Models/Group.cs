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
    public bool IsExpanded { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
