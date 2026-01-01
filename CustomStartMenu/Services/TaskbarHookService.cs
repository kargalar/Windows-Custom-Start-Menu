using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
        int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

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
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
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
            // Check if Windows Start Menu is being activated
            // The Start Menu window has class name "Windows.UI.Core.CoreWindow"
            // and contains "Start" in certain scenarios

            // Check if it's the Start menu being opened
            var startMenuHwnd = FindStartMenuWindow();
            if (startMenuHwnd != IntPtr.Zero && hwnd == startMenuHwnd)
            {
                // Debounce to prevent multiple triggers
                if ((DateTime.Now - _lastTriggerTime).TotalMilliseconds < DEBOUNCE_MS)
                    return;

                _lastTriggerTime = DateTime.Now;
                Debug.WriteLine("Start menu activation detected - intercepting");

                // Hide the Windows Start Menu and show our custom menu
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    // Give it a moment then trigger our menu
                    Task.Delay(50).ContinueWith(_ =>
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

    private IntPtr FindStartMenuWindow()
    {
        // Windows 11 Start Menu window
        var hwnd = FindWindow("Windows.UI.Core.CoreWindow", "Start");
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
