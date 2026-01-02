using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CustomStartMenu.Models;
using CustomStartMenu.Services;
using CustomStartMenu.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace CustomStartMenu;

public partial class App : Application
{
    private static Mutex? _mutex;
    private const string MutexName = "CustomStartMenu_SingleInstance_Mutex";
    
    private TaskbarIcon? _trayIcon;
    private StartMenuWindow? _startMenuWindow;
    private KeyboardHookService? _keyboardHookService;
    private TaskbarHookService? _taskbarHookService;
    private StartupService? _startupService;
    private ContextMenuService? _contextMenuService;
    private PinnedItemsService? _pinnedItemsService;
    private SettingsService? _settingsService;

    public static RoutedCommand ShowMenuCommand { get; } = new();

    public static App Instance => (App)Current;

    public bool IsMenuVisible => _startMenuWindow?.IsVisible ?? false;

    public PinnedItemsService PinnedItemsService => _pinnedItemsService!;
    public SettingsService SettingsService => _settingsService!;
    public KeyboardHookService KeyboardHookService => _keyboardHookService!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check for context menu arguments FIRST (before mutex check)
        // These commands should work even if another instance is running
        var args = e.Args;
        if (args.Length >= 2)
        {
            if (args[0] == "--pin")
            {
                var pathToPin = args[1];
                HandlePinFromContextMenu(pathToPin);
                return;
            }
            else if (args[0] == "--unpin")
            {
                var pathToUnpin = args[1];
                HandleUnpinFromContextMenu(pathToUnpin);
                return;
            }
            else if (args[0] == "--toggle")
            {
                var pathToToggle = args[1];
                HandleToggleFromContextMenu(pathToToggle);
                return;
            }
            else if (args[0] == "--check-pinned")
            {
                // Used by context menu to check if item is pinned
                var pathToCheck = args[1];
                CheckPinnedStatus(pathToCheck);
                return;
            }
        }

        // Check for single instance (only for normal startup, not context menu commands)
        _mutex = new Mutex(true, MutexName, out bool isNewInstance);
        
        if (!isNewInstance)
        {
            // Another instance is already running, exit silently
            Shutdown();
            return;
        }

        // Initialize services
        _settingsService = new SettingsService();
        _startupService = new StartupService();
        _contextMenuService = new ContextMenuService();
        _pinnedItemsService = new PinnedItemsService();

        // Apply saved theme color
        ApplyThemeColor(_settingsService.Settings.AccentColor);

        // Register context menu if not already
        if (!_contextMenuService.IsContextMenuRegistered())
        {
            _contextMenuService.RegisterContextMenu();
        }

        // Setup keyboard hook (must be before StartMenuWindow creation)
        _keyboardHookService = new KeyboardHookService();
        _keyboardHookService.UpdateHotkey(
            _settingsService.Settings.OpenMenuHotkey,
            _settingsService.Settings.OverrideWindowsStartButton);
        _keyboardHookService.HotkeyPressed += OnHotkeyPressed;
        _keyboardHookService.StartHook();

        // Create the start menu window (hidden initially)
        _startMenuWindow = new StartMenuWindow();
        _startMenuWindow.Hide();

        // Setup tray icon
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        
        // Update startup menu item
        UpdateStartupMenuItem();

        // Setup taskbar hook for Start button clicks
        _taskbarHookService = new TaskbarHookService();
        _taskbarHookService.StartButtonClicked += OnStartButtonClicked;
        if (_settingsService.Settings.OverrideWindowsStartButton)
        {
            _taskbarHookService.StartHook();
        }

        // Listen for settings changes
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var settings = _settingsService!.Settings;

            // Update keyboard hook
            _keyboardHookService?.UpdateHotkey(settings.OpenMenuHotkey, settings.OverrideWindowsStartButton);

            // Update taskbar hook
            if (_taskbarHookService != null)
            {
                _taskbarHookService.IsEnabled = settings.OverrideWindowsStartButton;
            }

            // Update theme color
            ApplyThemeColor(settings.AccentColor);
        });
    }

    /// <summary>
    /// Apply theme accent color to application resources
    /// </summary>
    public void ApplyThemeColor(string hexColor)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            Resources["AccentColor"] = color;
            Resources["AccentBrush"] = new SolidColorBrush(color);
        }
        catch
        {
            // Fallback to default blue
            var defaultColor = (Color)ColorConverter.ConvertFromString("#0078D4");
            Resources["AccentColor"] = defaultColor;
            Resources["AccentBrush"] = new SolidColorBrush(defaultColor);
        }
    }

    private void HandlePinFromContextMenu(string path)
    {
        try
        {
            // Initialize pinned items service just to add the pin
            // The FileSystemWatcher in the running instance will detect the change
            var pinnedService = new PinnedItemsService();
            
            // Check if already pinned - if so, do nothing
            if (pinnedService.IsPinned(path))
            {
                System.Diagnostics.Debug.WriteLine($"Already pinned: {path}");
                pinnedService.Dispose();
                Shutdown();
                return;
            }
            
            // Use default 6 columns for external pin operations
            pinnedService.AddPin(path, null, null, 6);
            pinnedService.Dispose();

            // Show notification or just exit
            System.Diagnostics.Debug.WriteLine($"Pinned: {path}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to pin: {ex.Message}");
        }
        finally
        {
            Shutdown();
        }
    }

    private void HandleUnpinFromContextMenu(string path)
    {
        try
        {
            // Initialize pinned items service just to remove the pin
            // The FileSystemWatcher in the running instance will detect the change
            var pinnedService = new PinnedItemsService();
            
            if (pinnedService.IsPinned(path))
            {
                pinnedService.RemovePin(path);
                System.Diagnostics.Debug.WriteLine($"Unpinned: {path}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Not pinned: {path}");
            }
            
            pinnedService.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to unpin: {ex.Message}");
        }
        finally
        {
            Shutdown();
        }
    }

    private void HandleToggleFromContextMenu(string path)
    {
        try
        {
            var pinnedService = new PinnedItemsService();
            
            if (pinnedService.IsPinned(path))
            {
                // Already pinned - remove it
                pinnedService.RemovePin(path);
                System.Diagnostics.Debug.WriteLine($"Toggled OFF (unpinned): {path}");
            }
            else
            {
                // Not pinned - add it
                pinnedService.AddPin(path, null, null, 6);
                System.Diagnostics.Debug.WriteLine($"Toggled ON (pinned): {path}");
            }
            
            pinnedService.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to toggle pin: {ex.Message}");
        }
        finally
        {
            Shutdown();
        }
    }

    private void CheckPinnedStatus(string path)
    {
        try
        {
            var pinnedService = new PinnedItemsService();
            var isPinned = pinnedService.IsPinned(path);
            pinnedService.Dispose();
            
            // Exit with code 0 if pinned, 1 if not pinned
            // This can be used by scripts or other tools to check status
            Environment.ExitCode = isPinned ? 0 : 1;
            System.Diagnostics.Debug.WriteLine($"Check pinned status for {path}: {isPinned}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to check pinned status: {ex.Message}");
            Environment.ExitCode = 2; // Error
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ToggleStartMenu();
        });
    }

    private void OnStartButtonClicked(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Small delay to ensure Windows Start Menu is closed
            Task.Delay(100).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!IsMenuVisible)
                    {
                        ShowStartMenu();
                    }
                });
            });
        });
    }

    public void ToggleStartMenu()
    {
        if (_startMenuWindow == null) return;

        if (_startMenuWindow.IsVisible)
        {
            _startMenuWindow.HideMenu();
        }
        else
        {
            _startMenuWindow.ShowMenu();
        }
    }

    public void ShowStartMenu()
    {
        _startMenuWindow?.ShowMenu();
    }

    public void HideStartMenu()
    {
        _startMenuWindow?.HideMenu();
    }

    private void UpdateStartupMenuItem()
    {
        if (_trayIcon?.ContextMenu != null)
        {
            foreach (var item in _trayIcon.ContextMenu.Items)
            {
                if (item is System.Windows.Controls.MenuItem menuItem && menuItem.Name == "StartupMenuItem")
                {
                    menuItem.IsChecked = _startupService?.IsStartupEnabled() ?? false;
                    break;
                }
            }
        }
    }

    private void TrayMenu_Open_Click(object sender, RoutedEventArgs e)
    {
        ShowStartMenu();
    }

    private void TrayMenu_Startup_Click(object sender, RoutedEventArgs e)
    {
        if (_startupService == null) return;

        if (_startupService.IsStartupEnabled())
        {
            _startupService.DisableStartup();
        }
        else
        {
            _startupService.EnableStartup();
        }

        UpdateStartupMenuItem();
    }

    private void TrayMenu_Exit_Click(object sender, RoutedEventArgs e)
    {
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Cleanup
        _keyboardHookService?.StopHook();
        _keyboardHookService?.Dispose();
        _taskbarHookService?.StopHook();
        _taskbarHookService?.Dispose();
        _settingsService?.Dispose();
        _trayIcon?.Dispose();
        _startMenuWindow?.Close();
        
        // Release mutex
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        base.OnExit(e);
    }
}
