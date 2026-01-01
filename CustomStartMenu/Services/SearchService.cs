using System.IO;
using CustomStartMenu.Models;

namespace CustomStartMenu.Services;

/// <summary>
/// Searches for files, folders, and applications on the PC
/// </summary>
public class SearchService
{
    private readonly List<string> _searchPaths;
    private List<SearchResult>? _cachedApps;
    private DateTime _lastCacheTime;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    public SearchService()
    {
        _searchPaths = new List<string>
        {
            // Start Menu locations
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            // Desktop
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            // Program Files
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
    }

    public async Task<List<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResult>();

        query = query.ToLowerInvariant();
        var results = new List<SearchResult>();

        // Search in cached apps first (fast)
        var apps = await GetCachedAppsAsync(cancellationToken);
        results.AddRange(apps.Where(a => 
            a.Name.ToLowerInvariant().Contains(query) ||
            Path.GetFileName(a.Path).ToLowerInvariant().Contains(query))
            .Select(a => new SearchResult
            {
                Name = a.Name,
                Path = a.Path,
                Type = a.Type,
                Score = CalculateScore(a.Name, query)
            }));

        // Sort by score (best matches first)
        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Name)
            .Take(20)
            .ToList();
    }

    private async Task<List<SearchResult>> GetCachedAppsAsync(CancellationToken cancellationToken)
    {
        if (_cachedApps != null && DateTime.Now - _lastCacheTime < _cacheExpiry)
            return _cachedApps;

        _cachedApps = await Task.Run(() => IndexApplications(cancellationToken), cancellationToken);
        _lastCacheTime = DateTime.Now;
        return _cachedApps;
    }

    private List<SearchResult> IndexApplications(CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var basePath in _searchPaths)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!Directory.Exists(basePath)) continue;

            try
            {
                // Get shortcuts (.lnk) from Start Menu
                var shortcuts = Directory.EnumerateFiles(basePath, "*.lnk", SearchOption.AllDirectories);
                foreach (var shortcut in shortcuts)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (seen.Contains(shortcut)) continue;
                    seen.Add(shortcut);

                    var name = Path.GetFileNameWithoutExtension(shortcut);
                    // Skip uninstall shortcuts
                    if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;

                    results.Add(new SearchResult
                    {
                        Name = name,
                        Path = shortcut,
                        Type = SearchResultType.Application
                    });
                }

                // Get executables from Program Files (only top level)
                if (basePath.Contains("Program Files"))
                {
                    var exeFiles = Directory.EnumerateFiles(basePath, "*.exe", SearchOption.TopDirectoryOnly);
                    foreach (var exe in exeFiles)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        if (seen.Contains(exe)) continue;
                        seen.Add(exe);

                        results.Add(new SearchResult
                        {
                            Name = Path.GetFileNameWithoutExtension(exe),
                            Path = exe,
                            Type = SearchResultType.Application
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error indexing {basePath}: {ex.Message}");
            }
        }

        return results;
    }

    private double CalculateScore(string name, string query)
    {
        var lowerName = name.ToLowerInvariant();
        
        // Exact match
        if (lowerName == query) return 100;
        
        // Starts with query
        if (lowerName.StartsWith(query)) return 80 + (query.Length / (double)lowerName.Length) * 10;
        
        // Contains query
        if (lowerName.Contains(query)) return 50 + (query.Length / (double)lowerName.Length) * 10;
        
        // Word starts with query
        var words = lowerName.Split(' ', '-', '_');
        if (words.Any(w => w.StartsWith(query))) return 60;
        
        return 0;
    }

    public void RefreshCache()
    {
        _cachedApps = null;
    }
}
