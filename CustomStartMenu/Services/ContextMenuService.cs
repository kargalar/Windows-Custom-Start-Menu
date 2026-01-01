using Microsoft.Win32;
using System.Diagnostics;

namespace CustomStartMenu.Services;

/// <summary>
/// Manages Windows Explorer context menu integration.
/// Adds a single "Custom Start Menu" toggle entry to right-click menu for files and folders.
/// The entry text changes based on whether the item is already pinned or not.
/// </summary>
public class ContextMenuService
{
    private const string MenuText = "Custom Start Menu";
    
    // Registry keys for .exe files
    private const string RegistryKeyExe = @"SOFTWARE\Classes\exefile\shell\CustomStartMenu";
    
    // Registry keys for folders
    private const string RegistryKeyFolder = @"SOFTWARE\Classes\Directory\shell\CustomStartMenu";
    
    // Registry keys for all files (*)
    private const string RegistryKeyAllFiles = @"SOFTWARE\Classes\*\shell\CustomStartMenu";

    // Legacy keys to clean up (old submenu format)
    private const string LegacyRegistryKeyExe = @"SOFTWARE\Classes\exefile\shell\CustomStartMenuPin";
    private const string LegacyRegistryKeyFolder = @"SOFTWARE\Classes\Directory\shell\CustomStartMenuPin";

    public bool IsContextMenuRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyExe);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public bool RegisterContextMenu()
    {
        try
        {
            var exePath = GetExecutablePath();
            
            // Clean up legacy entries first
            CleanupLegacyEntries();
            
            // Register for .exe files
            RegisterSingleMenuEntry(RegistryKeyExe, exePath);
            
            // Register for folders
            RegisterSingleMenuEntry(RegistryKeyFolder, exePath);
            
            // Register for all files (to support .url and other file types)
            RegisterSingleMenuEntry(RegistryKeyAllFiles, exePath);

            Debug.WriteLine("Context menu registered successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register context menu: {ex.Message}");
            return false;
        }
    }

    public bool UnregisterContextMenu()
    {
        try
        {
            // Remove menu entries
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyExe, false);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyFolder, false);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyAllFiles, false);
            
            // Clean up legacy entries
            CleanupLegacyEntries();
            
            Debug.WriteLine("Context menu unregistered successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to unregister context menu: {ex.Message}");
            return false;
        }
    }

    private void CleanupLegacyEntries()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(LegacyRegistryKeyExe, false);
            Registry.CurrentUser.DeleteSubKeyTree(LegacyRegistryKeyFolder, false);
        }
        catch
        {
            // Ignore errors when cleaning up legacy entries
        }
    }

    private void RegisterSingleMenuEntry(string registryKey, string exePath)
    {
        // Create single menu entry (no submenu)
        using var menuKey = Registry.CurrentUser.CreateSubKey(registryKey);
        if (menuKey == null) throw new Exception($"Failed to create registry key: {registryKey}");

        menuKey.SetValue("", MenuText);
        menuKey.SetValue("Icon", $"\"{exePath}\"");

        // Create command - use --toggle to let the app decide pin or unpin
        using var commandKey = menuKey.CreateSubKey("command");
        if (commandKey == null) throw new Exception("Failed to create command subkey");
        commandKey.SetValue("", $"\"{exePath}\" --toggle \"%1\"");
    }

    private string GetExecutablePath()
    {
        var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
        
        // For single-file published apps
        if (string.IsNullOrEmpty(location) || location.EndsWith(".dll"))
        {
            location = Environment.ProcessPath ?? "";
        }
        
        return location;
    }
}
