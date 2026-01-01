using Microsoft.Win32;
using System.Diagnostics;

namespace CustomStartMenu.Services;

/// <summary>
/// Manages Windows Explorer context menu integration.
/// Adds "Custom Start Menu'ye Pinle" option to right-click menu for exe files and folders.
/// </summary>
public class ContextMenuService
{
    private const string MenuTextExe = "Custom Start Menu'ye Pinle";
    private const string MenuTextFolder = "Custom Start Menu'ye Pinle";
    private const string RegistryKeyExe = @"SOFTWARE\Classes\exefile\shell\CustomStartMenuPin";
    private const string RegistryKeyFolder = @"SOFTWARE\Classes\Directory\shell\CustomStartMenuPin";

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
            
            // Register for .exe files
            RegisterMenuEntry(RegistryKeyExe, MenuTextExe, exePath, "--pin \"%1\"");
            
            // Register for folders
            RegisterMenuEntry(RegistryKeyFolder, MenuTextFolder, exePath, "--pin \"%1\"");

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
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyExe, false);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyFolder, false);
            
            Debug.WriteLine("Context menu unregistered successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to unregister context menu: {ex.Message}");
            return false;
        }
    }

    private void RegisterMenuEntry(string registryKey, string menuText, string exePath, string arguments)
    {
        using var key = Registry.CurrentUser.CreateSubKey(registryKey);
        if (key == null) throw new Exception($"Failed to create registry key: {registryKey}");

        key.SetValue("", menuText);
        key.SetValue("Icon", $"\"{exePath}\"");

        using var commandKey = key.CreateSubKey("command");
        if (commandKey == null) throw new Exception($"Failed to create command subkey");

        commandKey.SetValue("", $"\"{exePath}\" {arguments}");
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
