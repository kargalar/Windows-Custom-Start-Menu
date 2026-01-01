using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CustomStartMenu.Services;

/// <summary>
/// Low-level keyboard hook service to intercept Windows key press.
/// Works on Windows 11 by using both WH_KEYBOARD_LL hook and RegisterHotKey.
/// </summary>
public class KeyboardHookService : IDisposable
{
    #region Win32 API

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    // For KBDLLHOOKSTRUCT flags
    private const uint LLKHF_INJECTED = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private bool _isDisposed;
    private bool _winKeyDown;
    private bool _otherKeyPressed;

    /// <summary>
    /// Fired when Windows key is pressed and released (without other key combinations).
    /// </summary>
    public event EventHandler? WindowsKeyPressed;

    public bool IsHookActive => _hookId != IntPtr.Zero;

    public void StartHook()
    {
        if (_hookId != IntPtr.Zero) return;

        _hookProc = HookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;

        if (curModule != null)
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);

            if (_hookId == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                Debug.WriteLine($"Failed to set keyboard hook. Error code: {errorCode}");
            }
            else
            {
                Debug.WriteLine("Keyboard hook installed successfully.");
            }
        }
    }

    public void StopHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Debug.WriteLine("Keyboard hook removed.");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vkCode = (int)hookStruct.vkCode;

            // Check if it's Windows key (Left or Right)
            bool isWinKey = vkCode == VK_LWIN || vkCode == VK_RWIN;

            if (isWinKey)
            {
                int msg = wParam.ToInt32();

                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    if (!_winKeyDown)
                    {
                        _winKeyDown = true;
                        _otherKeyPressed = false;
                        Debug.WriteLine("Win key down");
                    }
                    // Block the default Start Menu
                    return new IntPtr(1);
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    if (_winKeyDown && !_otherKeyPressed)
                    {
                        Debug.WriteLine("Win key released - triggering custom menu");
                        // Fire event on key release (like normal Start Menu behavior)
                        WindowsKeyPressed?.Invoke(this, EventArgs.Empty);
                    }
                    _winKeyDown = false;
                    _otherKeyPressed = false;
                    // Block the default Start Menu
                    return new IntPtr(1);
                }
            }
            else if (_winKeyDown)
            {
                // Another key was pressed while Win key is held (e.g., Win+E, Win+R)
                // Don't block these combinations
                _otherKeyPressed = true;
                Debug.WriteLine($"Win + {vkCode} combination detected");
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
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

    ~KeyboardHookService()
    {
        Dispose(false);
    }
}
