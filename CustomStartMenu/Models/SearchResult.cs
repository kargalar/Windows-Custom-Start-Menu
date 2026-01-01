namespace CustomStartMenu.Models;

/// <summary>
/// Represents a search result item
/// </summary>
public class SearchResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public SearchResultType Type { get; set; }
    public double Score { get; set; }
}

public enum SearchResultType
{
    Application,
    Folder,
    File,
    Setting
}
