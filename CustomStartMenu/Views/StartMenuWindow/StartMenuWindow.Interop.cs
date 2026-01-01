using System.Runtime.InteropServices;

namespace CustomStartMenu.Views;

/// <summary>
/// Win32 API definitions and interop methods for window positioning and power actions
/// </summary>
public partial class StartMenuWindow
{
    #region Win32 API for positioning

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const uint ABE_BOTTOM = 3;
    private const uint ABE_TOP = 1;
    private const uint ABE_LEFT = 0;
    private const uint ABE_RIGHT = 2;

    #endregion

    #region Power Actions Win32

    [DllImport("user32.dll")]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("PowrProf.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private const uint EWX_LOGOFF = 0x00000000;

    #endregion

    private (uint Edge, RECT Rect) GetTaskbarInfo()
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>()
        };

        SHAppBarMessage(ABM_GETTASKBARPOS, ref data);

        return (data.uEdge, data.rc);
    }
}
