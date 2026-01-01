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
            
            // Windows 11 Start menu uses "Windows.UI.Core.CoreWindow" with "Start" title
            if (classNameStr == "Windows.UI.Core.CoreWindow")
            {
                var startMenuHwnd = FindWindow("Windows.UI.Core.CoreWindow", "Start");
                if (hwnd == startMenuHwnd && IsWindowVisible(hwnd))
                {
                    isStartMenu = true;
                }
                
                // Turkish locale
                if (!isStartMenu)
                {
                    startMenuHwnd = FindWindow("Windows.UI.Core.CoreWindow", "Başlat");
                    if (hwnd == startMenuHwnd && IsWindowVisible(hwnd))
                    {
                        isStartMenu = true;
                    }
                }
            }

            if (isStartMenu)
            {
                // Debounce to prevent multiple triggers
                if ((DateTime.Now - _lastTriggerTime).TotalMilliseconds < DEBOUNCE_MS)
                    return;

                _lastTriggerTime = DateTime.Now;
                Debug.WriteLine($"Start menu detected - intercepting (class: {classNameStr})");

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
            // Find and hide the Start Menu window
            var startMenuHwnd = FindWindow("Windows.UI.Core.CoreWindow", "Start");
            if (startMenuHwnd == IntPtr.Zero)
                startMenuHwnd = FindWindow("Windows.UI.Core.CoreWindow", "Başlat");

            if (startMenuHwnd != IntPtr.Zero && IsWindowVisible(startMenuHwnd))
            {
                // Try to hide the window directly
                ShowWindow(startMenuHwnd, SW_HIDE);
                Debug.WriteLine("Windows Start Menu hidden");
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
