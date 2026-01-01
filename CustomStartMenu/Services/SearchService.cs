using System.IO;
using Microsoft.Win32;
using Windows.Management.Deployment;
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

        // Index from file system paths
        IndexFileSystemApps(results, seen, cancellationToken);
        
        // Index from Windows Registry
        IndexRegistryApps(results, seen, cancellationToken);
        
        // Index from AppData folders (user-installed apps)
        IndexAppDataApps(results, seen, cancellationToken);
        
        // Index UWP/Store applications
        IndexUwpApps(results, seen, cancellationToken);

        return results;
    }

    private void IndexFileSystemApps(List<SearchResult> results, HashSet<string> seen, CancellationToken cancellationToken)
    {
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
    }

    /// <summary>
    /// Searches Windows Registry for installed applications
    /// </summary>
    private void IndexRegistryApps(List<SearchResult> results, HashSet<string> seen, CancellationToken cancellationToken)
    {
        // Registry paths for installed applications
        var registryPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        var registryRoots = new[] { Registry.LocalMachine, Registry.CurrentUser };

        foreach (var root in registryRoots)
        {
            foreach (var registryPath in registryPaths)
            {
                if (cancellationToken.IsCancellationRequested) return;

                try
                {
                    using var key = root.OpenSubKey(registryPath);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        if (cancellationToken.IsCancellationRequested) return;

                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var displayName = subKey.GetValue("DisplayName") as string;
                            var installLocation = subKey.GetValue("InstallLocation") as string;
                            var displayIcon = subKey.GetValue("DisplayIcon") as string;

                            // Skip entries without display name
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            // Skip system components and updates
                            var systemComponent = subKey.GetValue("SystemComponent");
                            if (systemComponent != null && (int)systemComponent == 1) continue;

                            // Try to find executable path
                            var exePath = FindExecutablePath(installLocation, displayIcon, displayName);
                            if (string.IsNullOrEmpty(exePath)) continue;

                            // Skip if already seen
                            if (seen.Contains(exePath)) continue;
                            seen.Add(exePath);

                            results.Add(new SearchResult
                            {
                                Name = displayName,
                                Path = exePath,
                                Type = SearchResultType.Application
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error reading registry subkey {subKeyName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error accessing registry path {registryPath}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Attempts to find the executable path from registry information
    /// </summary>
    private string? FindExecutablePath(string? installLocation, string? displayIcon, string displayName)
    {
        // Try DisplayIcon first (often contains the exe path)
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            // DisplayIcon can be "path.exe" or "path.exe,0" (with icon index)
            var iconPath = displayIcon.Split(',')[0].Trim('"');
            if (File.Exists(iconPath) && iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return iconPath;
            }
        }

        // Try InstallLocation
        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            // Look for exe with similar name to display name
            var normalizedName = displayName.Replace(" ", "").ToLowerInvariant();
            
            try
            {
                var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                
                // First try to find exact match
                foreach (var exe in exeFiles)
                {
                    var exeName = Path.GetFileNameWithoutExtension(exe).Replace(" ", "").ToLowerInvariant();
                    if (exeName == normalizedName || normalizedName.Contains(exeName) || exeName.Contains(normalizedName))
                    {
                        return exe;
                    }
                }

                // Return first exe if no match found
                if (exeFiles.Length > 0)
                {
                    return exeFiles[0];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching install location {installLocation}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Searches AppData folders for user-installed applications
    /// </summary>
    private void IndexAppDataApps(List<SearchResult> results, HashSet<string> seen, CancellationToken cancellationToken)
    {
        var appDataPaths = new List<string>();

        // %LocalAppData%\Programs - common location for user-installed apps (e.g., VS Code, Discord)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programsPath = Path.Combine(localAppData, "Programs");
        if (Directory.Exists(programsPath))
        {
            appDataPaths.Add(programsPath);
        }

        // %AppData%\Microsoft\Windows\Start Menu\Programs - user Start Menu shortcuts
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var startMenuPath = Path.Combine(roamingAppData, @"Microsoft\Windows\Start Menu\Programs");
        if (Directory.Exists(startMenuPath))
        {
            appDataPaths.Add(startMenuPath);
        }

        foreach (var basePath in appDataPaths)
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                // Search for .exe files
                var exeFiles = Directory.EnumerateFiles(basePath, "*.exe", SearchOption.AllDirectories);
                foreach (var exe in exeFiles)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    
                    // Skip uninstaller executables
                    var fileName = Path.GetFileName(exe).ToLowerInvariant();
                    if (fileName.Contains("uninstall") || fileName.Contains("update")) continue;
                    
                    if (seen.Contains(exe)) continue;
                    seen.Add(exe);

                    results.Add(new SearchResult
                    {
                        Name = Path.GetFileNameWithoutExtension(exe),
                        Path = exe,
                        Type = SearchResultType.Application
                    });
                }

                // Search for .lnk shortcuts
                var shortcuts = Directory.EnumerateFiles(basePath, "*.lnk", SearchOption.AllDirectories);
                foreach (var shortcut in shortcuts)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    
                    var name = Path.GetFileNameWithoutExtension(shortcut);
                    if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    
                    if (seen.Contains(shortcut)) continue;
                    seen.Add(shortcut);

                    results.Add(new SearchResult
                    {
                        Name = name,
                        Path = shortcut,
                        Type = SearchResultType.Application
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error indexing AppData path {basePath}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Searches for UWP/Microsoft Store applications using PackageManager
    /// </summary>
    private void IndexUwpApps(List<SearchResult> results, HashSet<string> seen, CancellationToken cancellationToken)
    {
        try
        {
            var packageManager = new PackageManager();
            var packages = packageManager.FindPackagesForUser(string.Empty);

            foreach (var package in packages)
            {
                if (cancellationToken.IsCancellationRequested) return;

                try
                {
                    // Skip framework packages and system packages
                    if (package.IsFramework || package.IsResourcePackage) continue;
                    
                    // Skip packages without a display name
                    var displayName = package.DisplayName;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    // Skip Microsoft system packages (but keep user apps like Xbox, Photos, etc.)
                    var packageName = package.Id.Name;
                    if (packageName.StartsWith("Microsoft.NET", StringComparison.OrdinalIgnoreCase) ||
                        packageName.StartsWith("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase) ||
                        packageName.StartsWith("Microsoft.UI", StringComparison.OrdinalIgnoreCase) ||
                        packageName.StartsWith("Microsoft.Services", StringComparison.OrdinalIgnoreCase) ||
                        packageName.Contains(".NET", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Create shell:AppsFolder launch URI
                    var appUserModelId = $"{package.Id.FamilyName}!App";
                    var launchUri = $"shell:AppsFolder\\{appUserModelId}";

                    // Skip if already seen (by family name to avoid duplicates)
                    if (seen.Contains(package.Id.FamilyName)) continue;
                    seen.Add(package.Id.FamilyName);

                    results.Add(new SearchResult
                    {
                        Name = displayName,
                        Path = launchUri,
                        Type = SearchResultType.Application
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing UWP package: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating UWP packages: {ex.Message}");
        }
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
