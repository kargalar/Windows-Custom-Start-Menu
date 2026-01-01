using Microsoft.Win32;
using System.Diagnostics;

namespace CustomStartMenu.Services;

/// <summary>
/// Manages Windows Explorer context menu integration.
/// Adds "Custom Start Menu" submenu with Pin/Unpin options to right-click menu for exe files and folders.
/// </summary>
public class ContextMenuService
{
    private const string MenuTextPin = "Pinle";
    private const string MenuTextUnpin = "Kaldır";
    private const string SubMenuText = "Custom Start Menu";
    
    // Registry keys for .exe files
    private const string RegistryKeyExe = @"SOFTWARE\Classes\exefile\shell\CustomStartMenu";
    private const string RegistryKeyExePin = @"SOFTWARE\Classes\exefile\shell\CustomStartMenu\shell\Pin";
    private const string RegistryKeyExeUnpin = @"SOFTWARE\Classes\exefile\shell\CustomStartMenu\shell\Unpin";
    
    // Registry keys for folders
    private const string RegistryKeyFolder = @"SOFTWARE\Classes\Directory\shell\CustomStartMenu";
    private const string RegistryKeyFolderPin = @"SOFTWARE\Classes\Directory\shell\CustomStartMenu\shell\Pin";
    private const string RegistryKeyFolderUnpin = @"SOFTWARE\Classes\Directory\shell\CustomStartMenu\shell\Unpin";
    
    // Registry keys for all files (*)
    private const string RegistryKeyAllFiles = @"SOFTWARE\Classes\*\shell\CustomStartMenu";
    private const string RegistryKeyAllFilesPin = @"SOFTWARE\Classes\*\shell\CustomStartMenu\shell\Pin";
    private const string RegistryKeyAllFilesUnpin = @"SOFTWARE\Classes\*\shell\CustomStartMenu\shell\Unpin";

    // Legacy keys to clean up
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
            RegisterSubMenuEntry(RegistryKeyExe, RegistryKeyExePin, RegistryKeyExeUnpin, exePath);
            
            // Register for folders
            RegisterSubMenuEntry(RegistryKeyFolder, RegistryKeyFolderPin, RegistryKeyFolderUnpin, exePath);
            
            // Register for all files (to support .url and other file types)
            RegisterSubMenuEntry(RegistryKeyAllFiles, RegistryKeyAllFilesPin, RegistryKeyAllFilesUnpin, exePath);

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
            // Remove new submenu entries
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

    private void RegisterSubMenuEntry(string parentKey, string pinKey, string unpinKey, string exePath)
    {
        // Create parent submenu
        using var parentMenuKey = Registry.CurrentUser.CreateSubKey(parentKey);
        if (parentMenuKey == null) throw new Exception($"Failed to create registry key: {parentKey}");

        parentMenuKey.SetValue("MUIVerb", SubMenuText);
        parentMenuKey.SetValue("SubCommands", "");
        parentMenuKey.SetValue("Icon", $"\"{exePath}\"");

        // Create Pin submenu item
        using var pinMenuKey = Registry.CurrentUser.CreateSubKey(pinKey);
        if (pinMenuKey == null) throw new Exception($"Failed to create registry key: {pinKey}");
        
        pinMenuKey.SetValue("MUIVerb", MenuTextPin);
        pinMenuKey.SetValue("Icon", $"\"{exePath}\"");

        using var pinCommandKey = pinMenuKey.CreateSubKey("command");
        if (pinCommandKey == null) throw new Exception("Failed to create Pin command subkey");
        pinCommandKey.SetValue("", $"\"{exePath}\" --pin \"%1\"");

        // Create Unpin submenu item
        using var unpinMenuKey = Registry.CurrentUser.CreateSubKey(unpinKey);
        if (unpinMenuKey == null) throw new Exception($"Failed to create registry key: {unpinKey}");
        
        unpinMenuKey.SetValue("MUIVerb", MenuTextUnpin);
        unpinMenuKey.SetValue("Icon", $"\"{exePath}\"");

        using var unpinCommandKey = unpinMenuKey.CreateSubKey("command");
        if (unpinCommandKey == null) throw new Exception("Failed to create Unpin command subkey");
        unpinCommandKey.SetValue("", $"\"{exePath}\" --unpin \"%1\"");
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
