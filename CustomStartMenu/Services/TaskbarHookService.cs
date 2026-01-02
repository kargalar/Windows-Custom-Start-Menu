using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace CustomStartMenu.Services;

/// <summary>
/// Service to intercept clicks on the Windows taskbar Start button.
/// Uses UI Automation to detect when the Start button is clicked.
/// </summary>
public class TaskbarHookService : IDisposable
{
    #region Win32 API

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
        int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const int SW_HIDE = 0;
    
    // Virtual key codes
    private const byte VK_ESCAPE = 0x1B;
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    #endregion

    private IntPtr _winEventHook = IntPtr.Zero;
    private WinEventDelegate? _winEventDelegate;
    private bool _isEnabled;
    private bool _isDisposed;
    private DateTime _lastTriggerTime = DateTime.MinValue;
    private const int DEBOUNCE_MS = 500;

    /// <summary>
    /// Fired when the Start button is clicked
    /// </summary>
    public event EventHandler? StartButtonClicked;

    /// <summary>
    /// Enable or disable the taskbar hook
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                if (_isEnabled)
                    StartHook();
                else
                    StopHook();
            }
        }
    }

    public void StartHook()
    {
        if (_winEventHook != IntPtr.Zero) return;

        _winEventDelegate = WinEventProc;
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_SHOW,
            IntPtr.Zero, _winEventDelegate,
            0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (_winEventHook == IntPtr.Zero)
        {
            Debug.WriteLine("Failed to set taskbar hook");
        }
        else
        {
            Debug.WriteLine("Taskbar hook installed successfully");
            _isEnabled = true;
        }
    }

    public void StopHook()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
            Debug.WriteLine("Taskbar hook removed");
        }
        _isEnabled = false;
    }

    // Start menu window class names for different Windows versions
    private static readonly string[] StartMenuClassNames = new[]
    {
        "Windows.UI.Core.CoreWindow",           // Windows 10/11 main Start menu
        "Xaml_WindowedPopupClass",              // Windows 11 Start menu popups
        "Windows.UI.Composition.DesktopWindowContentBridge", // Windows 11 composition
        "LauncherTipWnd",                       // Windows 11 Start button flyout
        "Shell_TrayWnd",                        // Windows 10 taskbar
    };

    // Start menu window titles for different locales
    private static readonly string[] StartMenuTitles = new[]
    {
        "Start",                                // English
        "Başlat",                               // Turkish
        "Démarrer",                             // French
        "Inicio",                               // Spanish
        "Start-Menü",                           // German
        "スタート",                              // Japanese
        "시작",                                  // Korean
        "开始",                                  // Chinese (Simplified)
        "開始",                                  // Chinese (Traditional)
        "Пуск",                                 // Russian
    };

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_isEnabled) return;

        try
        {
            // Get window class name
            var className = new System.Text.StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            var classNameStr = className.ToString();

            // Detect Windows 10/11 Start Menu windows
            bool isStartMenu = false;
            
            // Check if this is a potential Start menu window class
            bool isPotentialStartMenuClass = StartMenuClassNames.Any(c => 
                classNameStr.Equals(c, StringComparison.OrdinalIgnoreCase));

            if (isPotentialStartMenuClass)
            {
                // For CoreWindow and similar classes, check the window title
                if (classNameStr == "Windows.UI.Core.CoreWindow" || 
                    classNameStr == "Xaml_WindowedPopupClass" ||
                    classNameStr == "Windows.UI.Composition.DesktopWindowContentBridge")
                {
                    foreach (var title in StartMenuTitles)
                    {
                        var startMenuHwnd = FindWindow(classNameStr, title);
                        if (hwnd == startMenuHwnd && IsWindowVisible(hwnd))
                        {
                            isStartMenu = true;
                            Debug.WriteLine($"Start menu detected via title match: {title}");
                            break;
                        }
                    }
                    
                    // Also check for Windows 11 22H2+ where title might be empty or different
                    if (!isStartMenu && IsWindowVisible(hwnd))
                    {
                        // Check if this window belongs to StartMenuExperienceHost
                        var processId = GetWindowProcessId(hwnd);
                        if (IsStartMenuProcess(processId))
                        {
                            isStartMenu = true;
                            Debug.WriteLine($"Start menu detected via process: StartMenuExperienceHost");
                        }
                    }
                }
                else if (classNameStr == "LauncherTipWnd")
                {
                    // Windows 11 Start button tooltip/flyout
                    if (IsWindowVisible(hwnd))
                    {
                        isStartMenu = true;
                        Debug.WriteLine("Start menu detected via LauncherTipWnd");
                    }
                }
            }

            if (isStartMenu)
            {
                // Debounce to prevent multiple triggers
                if ((DateTime.Now - _lastTriggerTime).TotalMilliseconds < DEBOUNCE_MS)
                    return;

                _lastTriggerTime = DateTime.Now;
                Debug.WriteLine($"Start menu intercepted (class: {classNameStr})");

                // Close Windows Start Menu immediately
                CloseWindowsStartMenu();

                // Show our custom menu
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    // Small delay to ensure Windows start menu is closed
                    Task.Delay(100).ContinueWith(_ =>
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            StartButtonClicked?.Invoke(this, EventArgs.Empty);
                        });
                    });
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in WinEventProc: {ex.Message}");
        }
    }

    /// <summary>
    /// Close the Windows Start Menu by sending Escape key
    /// </summary>
    private void CloseWindowsStartMenu()
    {
        try
        {
            // Try to hide all possible Start menu windows
            foreach (var className in StartMenuClassNames)
            {
                foreach (var title in StartMenuTitles)
                {
                    var startMenuHwnd = FindWindow(className, title);
                    if (startMenuHwnd != IntPtr.Zero && IsWindowVisible(startMenuHwnd))
                    {
                        ShowWindow(startMenuHwnd, SW_HIDE);
                        Debug.WriteLine($"Hidden Start Menu window: {className} - {title}");
                    }
                }
                
                // Also try with null title
                var hwnd = FindWindow(className, null);
                if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                {
                    var processId = GetWindowProcessId(hwnd);
                    if (IsStartMenuProcess(processId))
                    {
                        ShowWindow(hwnd, SW_HIDE);
                        Debug.WriteLine($"Hidden Start Menu window via process: {className}");
                    }
                }
            }

            // Also send Escape key to close any remaining UI
            keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error closing Windows Start Menu: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the process ID of a window
    /// </summary>
    private uint GetWindowProcessId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId;
    }

    /// <summary>
    /// Check if a process ID belongs to StartMenuExperienceHost or ShellExperienceHost
    /// </summary>
    private bool IsStartMenuProcess(uint processId)
    {
        try
        {
            var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName.ToLowerInvariant();
            return processName == "startmenuexperiencehost" || 
                   processName == "shellexperiencehost" ||
                   processName == "searchhost";
        }
        catch
        {
            return false;
        }
    }

    private IntPtr FindStartMenuWindow()
    {
        // Windows 11 Start Menu window
        var hwnd = FindWindow("Windows.UI.Core.CoreWindow", "Start");
        if (hwnd != IntPtr.Zero) return hwnd;
        
        // Turkish locale
        hwnd = FindWindow("Windows.UI.Core.CoreWindow", "Başlat");
        if (hwnd != IntPtr.Zero) return hwnd;

        // Try alternative class names
        hwnd = FindWindow("Shell_TrayWnd", null);
        if (hwnd != IntPtr.Zero)
        {
            // Find the Start button within the taskbar
            var startButton = FindWindowEx(hwnd, IntPtr.Zero, "Start", null);
            return startButton;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            StopHook();
            _isDisposed = true;
        }
    }

    ~TaskbarHookService()
    {
        Dispose(false);
    }
}
