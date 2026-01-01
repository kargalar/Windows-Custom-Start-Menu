using System.Windows;
using System.Windows.Input;
using CustomStartMenu.Services;
using CustomStartMenu.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace CustomStartMenu;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private StartMenuWindow? _startMenuWindow;
    private KeyboardHookService? _keyboardHookService;
    private StartupService? _startupService;
    private ContextMenuService? _contextMenuService;
    private PinnedItemsService? _pinnedItemsService;

    public static RoutedCommand ShowMenuCommand { get; } = new();

    public static App Instance => (App)Current;

    public bool IsMenuVisible => _startMenuWindow?.IsVisible ?? false;

    public PinnedItemsService PinnedItemsService => _pinnedItemsService!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check for --pin argument (from context menu)
        var args = e.Args;
        if (args.Length >= 2 && args[0] == "--pin")
        {
            var pathToPin = args[1];
            HandlePinFromContextMenu(pathToPin);
            return;
        }

        // Initialize services
        _startupService = new StartupService();
        _contextMenuService = new ContextMenuService();
        _pinnedItemsService = new PinnedItemsService();

        // Register context menu if not already
        if (!_contextMenuService.IsContextMenuRegistered())
        {
            _contextMenuService.RegisterContextMenu();
        }

        // Create the start menu window (hidden initially)
        _startMenuWindow = new StartMenuWindow();
        _startMenuWindow.Hide();

        // Setup tray icon
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        
        // Update startup menu item
        UpdateStartupMenuItem();

        // Setup keyboard hook
        _keyboardHookService = new KeyboardHookService();
        _keyboardHookService.WindowsKeyPressed += OnWindowsKeyPressed;
        _keyboardHookService.StartHook();
    }

    private void HandlePinFromContextMenu(string path)
    {
        try
        {
            // Initialize pinned items service just to add the pin
            // The FileSystemWatcher in the running instance will detect the change
            var pinnedService = new PinnedItemsService();
            pinnedService.AddPin(path);
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

    private void OnWindowsKeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ToggleStartMenu();
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
        _trayIcon?.Dispose();
        _startMenuWindow?.Close();

        base.OnExit(e);
    }
}
