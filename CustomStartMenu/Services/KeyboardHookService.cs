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
    
    // For keybd_event / SendInput
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint INPUT_KEYBOARD = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
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
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    #endregion

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private bool _isDisposed;
    private bool _winKeyDown;
    private bool _otherKeyPressed;
    private int _pendingKeyCode; // Store the key code that was pressed with Win

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

    private bool CheckModifiersMatch()
    {
        bool ctrlPressed = IsModifierPressed(VK_LCONTROL) || IsModifierPressed(VK_RCONTROL);
        bool altPressed = IsModifierPressed(VK_LMENU) || IsModifierPressed(VK_RMENU);
        bool shiftPressed = IsModifierPressed(VK_LSHIFT) || IsModifierPressed(VK_RSHIFT);
        bool winPressed = IsModifierPressed(VK_LWIN) || IsModifierPressed(VK_RWIN);

        return _hotkeyConfig.UseWinKey == winPressed &&
               _hotkeyConfig.Ctrl == ctrlPressed &&
               _hotkeyConfig.Alt == altPressed &&
               _hotkeyConfig.Shift == shiftPressed;
    }
    
    /// <summary>
    /// Simulates a Win + key combination
    /// </summary>
    private void SimulateWinKeyCombo(int keyCode)
    {
        var inputs = new INPUT[4];
        
        // Win key down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = (ushort)VK_LWIN;
        inputs[0].u.ki.dwFlags = 0;
        
        // Other key down
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = (ushort)keyCode;
        inputs[1].u.ki.dwFlags = 0;
        
        // Other key up
        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = (ushort)keyCode;
        inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;
        
        // Win key up
        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = (ushort)VK_LWIN;
        inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;
        
        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int vkCode = (int)hookStruct.vkCode;
        int msg = wParam.ToInt32();
        bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
        bool isWinKey = vkCode == VK_LWIN || vkCode == VK_RWIN;

        // === HOTKEY ASSIGNMENT MODE ===
        if (SuppressWinKey)
        {
            if (isKeyDown)
            {
                bool ctrlPressed = IsModifierPressed(VK_LCONTROL) || IsModifierPressed(VK_RCONTROL);
                bool altPressed = IsModifierPressed(VK_LMENU) || IsModifierPressed(VK_RMENU);
                bool shiftPressed = IsModifierPressed(VK_LSHIFT) || IsModifierPressed(VK_RSHIFT);
                bool winPressed = IsModifierPressed(VK_LWIN) || IsModifierPressed(VK_RWIN) || isWinKey;

                KeyPressedForAssignment?.Invoke(this, new KeyPressedEventArgs(vkCode, winPressed, ctrlPressed, altPressed, shiftPressed));
            }
            
            // Block Win key to prevent Windows Start menu
            if (isWinKey)
                return new IntPtr(1);
            
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // === TEXT CAPTURE MODE (when menu is open) ===
        if (CaptureTextInput && isKeyDown && !isWinKey)
        {
            bool ctrlPressed = IsModifierPressed(VK_LCONTROL) || IsModifierPressed(VK_RCONTROL);
            bool altPressed = IsModifierPressed(VK_LMENU) || IsModifierPressed(VK_RMENU);
            bool winPressed = IsModifierPressed(VK_LWIN) || IsModifierPressed(VK_RWIN);
            
            if (!ctrlPressed && !altPressed && !winPressed)
            {
                var character = VirtualKeyToChar((uint)vkCode, hookStruct.scanCode);
                if (character.HasValue && !char.IsControl(character.Value))
                {
                    CharacterInput?.Invoke(this, character.Value);
                }
            }
        }

        // === HOTKEY DETECTION ===
        // Case 1: Hotkey is Win key alone
        if (_hotkeyConfig.UseWinKey && _hotkeyConfig.KeyCode == 0 && 
            !_hotkeyConfig.Ctrl && !_hotkeyConfig.Alt && !_hotkeyConfig.Shift)
        {
            if (isWinKey)
            {
                if (isKeyDown)
                {
                    _winKeyDown = true;
                    _otherKeyPressed = false;
                    _pendingKeyCode = 0;
                    return new IntPtr(1); // Block Win key - don't let Windows see it
                }
                else if (isKeyUp)
                {
                    bool shouldTrigger = _winKeyDown && !_otherKeyPressed;
                    _winKeyDown = false;
                    
                    if (_otherKeyPressed && _pendingKeyCode > 0)
                    {
                        // User pressed Win + another key, simulate the Windows shortcut
                        _otherKeyPressed = false;
                        SimulateWinKeyCombo(_pendingKeyCode);
                        _pendingKeyCode = 0;
                        return new IntPtr(1);
                    }
                    
                    _otherKeyPressed = false;
                    _pendingKeyCode = 0;
                    
                    if (shouldTrigger)
                    {
                        HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    }
                    return new IntPtr(1); // Block Win key up too
                }
            }
            else if (_winKeyDown && isKeyDown)
            {
                // Another key pressed while our tracked Win is "held"
                // Don't process it now, wait for Win key release to simulate the combo
                _otherKeyPressed = true;
                _pendingKeyCode = vkCode;
                return new IntPtr(1); // Block this key too, we'll simulate Win+key later
            }
            else if (_winKeyDown && isKeyUp && vkCode == _pendingKeyCode)
            {
                // The other key was released - we'll simulate when Win is released
                return new IntPtr(1); // Block the key up
            }
            
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        
        // Case 2: Hotkey is Win + another key
        if (_hotkeyConfig.UseWinKey && _hotkeyConfig.KeyCode > 0)
        {
            if (isKeyDown && vkCode == _hotkeyConfig.KeyCode && CheckModifiersMatch())
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                return new IntPtr(1);
            }
            // Let all other keys pass through normally
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        
        // Case 3: Hotkey without Win key (e.g., Ctrl+Alt+Space)
        if (!_hotkeyConfig.UseWinKey && _hotkeyConfig.KeyCode > 0)
        {
            if (isKeyDown && vkCode == _hotkeyConfig.KeyCode && CheckModifiersMatch())
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                return new IntPtr(1);
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
