namespace CustomStartMenu.Models;

/// <summary>
/// Represents a tab that can contain pinned items
/// </summary>
public class Tab
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Yeni Sekme";
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
