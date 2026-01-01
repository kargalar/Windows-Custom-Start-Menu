using Microsoft.Win32;
using System.Reflection;

namespace CustomStartMenu.Services;

/// <summary>
/// Manages application startup with Windows.
/// </summary>
public class StartupService
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "CustomStartMenu";

    private string GetExecutablePath()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        
        // For single-file published apps, use the process path
        if (string.IsNullOrEmpty(location) || location.EndsWith(".dll"))
        {
            location = Environment.ProcessPath ?? "";
        }
        
        return location;
    }

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            var value = key?.GetValue(AppName) as string;
            
            if (string.IsNullOrEmpty(value)) return false;
            
            // Check if the path matches current executable
            var currentPath = GetExecutablePath();
            return value.Trim('"').Equals(currentPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void EnableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            var exePath = GetExecutablePath();
            key?.SetValue(AppName, $"\"{exePath}\"");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to enable startup: {ex.Message}");
        }
    }

    public void DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            key?.DeleteValue(AppName, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to disable startup: {ex.Message}");
        }
    }
}
