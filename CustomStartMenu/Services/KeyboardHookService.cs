using System.Diagnostics;
using System.Runtime.InteropServices;
using CustomStartMenu.Models;

namespace CustomStartMenu.Services;

/// <summary>
/// Event args for key pressed during assignment mode
/// </summary>
public class KeyPressedEventArgs : EventArgs
{
    public int VirtualKeyCode { get; }
    public bool IsWinKey { get; }
    public bool IsCtrlPressed { get; }
    public bool IsAltPressed { get; }
    public bool IsShiftPressed { get; }

    public KeyPressedEventArgs(int vkCode, bool isWinKey, bool ctrl, bool alt, bool shift)
    {
        VirtualKeyCode = vkCode;
        IsWinKey = isWinKey;
        IsCtrlPressed = ctrl;
        IsAltPressed = alt;
        IsShiftPressed = shift;
    }
}

/// <summary>
/// Low-level keyboard hook service to intercept Windows key press and custom hotkeys.
/// Works on Windows 11 by using WH_KEYBOARD_LL hook.
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
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;

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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    
    [DllImport("user32.dll")]
    private static extern int ToUnicode(uint virtualKeyCode, uint scanCode, byte[] keyboardState, 
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder receivingBuffer, int bufferSize, uint flags);
    
    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    #endregion

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private bool _isDisposed;
    private bool _winKeyDown;
    private bool _otherKeyPressed;

    // Hotkey configuration
    private HotkeyConfig _hotkeyConfig = new();
    private bool _overrideWindowsButton = true;

    /// <summary>
    /// When true, Win key presses are suppressed without triggering any action.
    /// Used during hotkey assignment to prevent Windows Start menu from opening.
    /// </summary>
    public bool SuppressWinKey { get; set; } = false;
    
    /// <summary>
    /// When true, captures printable characters globally and fires CharacterInput event.
    /// Used when menu is open to allow typing without focus.
    /// </summary>
    public bool CaptureTextInput { get; set; } = false;

    /// <summary>
    /// Fired when the configured hotkey is pressed.
    /// </summary>
    public event EventHandler? HotkeyPressed;
    
    /// <summary>
    /// Fired when a printable character is pressed while CaptureTextInput is active.
    /// </summary>
    public event EventHandler<char>? CharacterInput;

    /// <summary>
    /// Fired when any key is pressed while SuppressWinKey is active (for hotkey assignment).
    /// The int parameter is the virtual key code.
    /// </summary>
    public event EventHandler<KeyPressedEventArgs>? KeyPressedForAssignment;

    /// <summary>
    /// Legacy event name for compatibility
    /// </summary>
    public event EventHandler? WindowsKeyPressed
    {
        add => HotkeyPressed += value;
        remove => HotkeyPressed -= value;
    }

    public bool IsHookActive => _hookId != IntPtr.Zero;

    /// <summary>
    /// Update the hotkey configuration
    /// </summary>
    public void UpdateHotkey(HotkeyConfig config, bool overrideWindowsButton)
    {
        _hotkeyConfig = config ?? new HotkeyConfig();
        _overrideWindowsButton = overrideWindowsButton;
        Debug.WriteLine($"Hotkey updated: {_hotkeyConfig}, Override Windows: {_overrideWindowsButton}");
    }

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

    private bool IsModifierPressed(int vkCode)
    {
        return (GetAsyncKeyState(vkCode) & 0x8000) != 0;
    }
    
    private char? VirtualKeyToChar(uint vkCode, uint scanCode)
    {
        var keyboardState = new byte[256];
        GetKeyboardState(keyboardState);
        
        var stringBuilder = new System.Text.StringBuilder(2);
        int result = ToUnicode(vkCode, scanCode, keyboardState, stringBuilder, stringBuilder.Capacity, 0);
        
        if (result > 0)
        {
            return stringBuilder[0];
        }
        
        return null;
    }

    private bool CheckModifiers()
    {
        bool ctrlPressed = IsModifierPressed(VK_LCONTROL) || IsModifierPressed(VK_RCONTROL);
        bool altPressed = IsModifierPressed(VK_LMENU) || IsModifierPressed(VK_RMENU);
        bool shiftPressed = IsModifierPressed(VK_LSHIFT) || IsModifierPressed(VK_RSHIFT);
        bool winPressed = IsModifierPressed(VK_LWIN) || IsModifierPressed(VK_RWIN);

        // Check if configured modifiers match
        return (_hotkeyConfig.UseWinKey == winPressed || _hotkeyConfig.UseWinKey && _winKeyDown) &&
               _hotkeyConfig.Ctrl == ctrlPressed &&
               _hotkeyConfig.Alt == altPressed &&
               _hotkeyConfig.Shift == shiftPressed;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vkCode = (int)hookStruct.vkCode;
            int msg = wParam.ToInt32();

            // Check if it's Windows key (Left or Right)
            bool isWinKey = vkCode == VK_LWIN || vkCode == VK_RWIN;

            // If SuppressWinKey is enabled (hotkey assignment mode)
            if (SuppressWinKey)
            {
                // Only handle key down events
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    bool ctrlPressed = IsModifierPressed(VK_LCONTROL) || IsModifierPressed(VK_RCONTROL);
                    bool altPressed = IsModifierPressed(VK_LMENU) || IsModifierPressed(VK_RMENU);
                    bool shiftPressed = IsModifierPressed(VK_LSHIFT) || IsModifierPressed(VK_RSHIFT);
                    bool winPressed = IsModifierPressed(VK_LWIN) || IsModifierPressed(VK_RWIN) || isWinKey;

                    Debug.WriteLine($"Key pressed for assignment: {vkCode}, Win={winPressed}, Ctrl={ctrlPressed}, Alt={altPressed}, Shift={shiftPressed}");
                    
                    KeyPressedForAssignment?.Invoke(this, new KeyPressedEventArgs(vkCode, winPressed, ctrlPressed, altPressed, shiftPressed));
                }
                
                // Block Win key to prevent Windows Start menu
                if (isWinKey)
                {
                    return new IntPtr(1);
                }
                
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            // Capture text input when menu is open
            if (CaptureTextInput && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN))
            {
                // Don't capture if modifier keys are held (except Shift)
                bool ctrlPressed = IsModifierPressed(VK_LCONTROL) || IsModifierPressed(VK_RCONTROL);
                bool altPressed = IsModifierPressed(VK_LMENU) || IsModifierPressed(VK_RMENU);
                
                if (!ctrlPressed && !altPressed && !isWinKey && !_winKeyDown)
                {
                    // Convert virtual key to character
                    var character = VirtualKeyToChar((uint)vkCode, hookStruct.scanCode);
                    if (character.HasValue && !char.IsControl(character.Value))
                    {
                        CharacterInput?.Invoke(this, character.Value);
                    }
                }
            }

            // Should we intercept Win key? Either for override or because hotkey uses Win
            bool shouldInterceptWinKey = _overrideWindowsButton || _hotkeyConfig.UseWinKey;

            // Handle Windows key
            if (isWinKey && shouldInterceptWinKey)
            {
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
                    bool shouldTrigger = _winKeyDown && !_otherKeyPressed;
                    _winKeyDown = false;
                    _otherKeyPressed = false;

                    // Trigger if Win key only and that's the configured hotkey
                    if (shouldTrigger && _hotkeyConfig.UseWinKey && _hotkeyConfig.KeyCode == 0 &&
                        !_hotkeyConfig.Ctrl && !_hotkeyConfig.Alt && !_hotkeyConfig.Shift)
                    {
                        Debug.WriteLine("Win key released - triggering custom menu");
                        HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    }

                    // Block the default Start Menu
                    return new IntPtr(1);
                }
            }
            else if (_winKeyDown && !isWinKey && _hotkeyConfig.UseWinKey)
            {
                // Another key was pressed while Win key is held (and hotkey uses Win)
                _otherKeyPressed = true;
                Debug.WriteLine($"Win + {vkCode} combination detected");

                // Check if this is our configured hotkey combination
                if (_hotkeyConfig.KeyCode == vkCode && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN))
                {
                    if (CheckModifiers())
                    {
                        Debug.WriteLine($"Custom hotkey triggered: {_hotkeyConfig}");
                        HotkeyPressed?.Invoke(this, EventArgs.Empty);
                        return new IntPtr(1); // Block the key
                    }
                }
            }
            // Handle non-Win key hotkeys (e.g., Ctrl+Alt+Space)
            else if (!_hotkeyConfig.UseWinKey && _hotkeyConfig.KeyCode > 0)
            {
                if (vkCode == _hotkeyConfig.KeyCode && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN))
                {
                    if (CheckModifiers())
                    {
                        Debug.WriteLine($"Custom hotkey triggered: {_hotkeyConfig}");
                        HotkeyPressed?.Invoke(this, EventArgs.Empty);
                        return new IntPtr(1); // Block the key
                    }
                }
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
