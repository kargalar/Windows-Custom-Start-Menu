using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace CustomStartMenu.Views;

public partial class StartMenuWindow : Window
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

    private bool _isClosing;

    public StartMenuWindow()
    {
        InitializeComponent();
        SetUserName();
    }

    private void SetUserName()
    {
        try
        {
            UserNameText.Text = Environment.UserName;
        }
        catch
        {
            UserNameText.Text = "Kullanıcı";
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
    }

    private void PositionWindow()
    {
        // Get taskbar position and size
        var taskbarInfo = GetTaskbarInfo();

        // Get working area (screen minus taskbar)
        var workArea = SystemParameters.WorkArea;

        // Position based on taskbar location
        switch (taskbarInfo.Edge)
        {
            case ABE_BOTTOM:
                // Taskbar at bottom (most common)
                Left = workArea.Left + 12;
                Top = workArea.Bottom - Height;
                break;

            case ABE_TOP:
                // Taskbar at top
                Left = workArea.Left + 12;
                Top = workArea.Top;
                break;

            case ABE_LEFT:
                // Taskbar at left
                Left = workArea.Left;
                Top = workArea.Bottom - Height;
                break;

            case ABE_RIGHT:
                // Taskbar at right
                Left = workArea.Right - Width;
                Top = workArea.Bottom - Height;
                break;

            default:
                // Fallback: bottom-left corner
                Left = 12;
                Top = workArea.Bottom - Height;
                break;
        }
    }

    private (uint Edge, RECT Rect) GetTaskbarInfo()
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>()
        };

        SHAppBarMessage(ABM_GETTASKBARPOS, ref data);

        return (data.uEdge, data.rc);
    }

    public void ShowMenu()
    {
        if (_isClosing) return;

        PositionWindow();

        // Reset opacity for animation
        MainBorder.Opacity = 0;
        MainBorder.RenderTransform = new System.Windows.Media.TranslateTransform(0, 20);

        Show();
        Activate();

        // Animate in
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        var slideIn = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        MainBorder.BeginAnimation(OpacityProperty, fadeIn);
        ((System.Windows.Media.TranslateTransform)MainBorder.RenderTransform).BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty, slideIn);

        // Focus search box
        SearchBox.Focus();
        SearchBox.Clear();
    }

    public void HideMenu()
    {
        if (!IsVisible || _isClosing) return;

        _isClosing = true;

        // Animate out
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (s, e) =>
        {
            Hide();
            _isClosing = false;
        };

        MainBorder.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Hide when clicking outside
        HideMenu();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideMenu();
            e.Handled = true;
        }
        else if (e.Key == Key.LWin || e.Key == Key.RWin)
        {
            // Win key pressed again - toggle off
            HideMenu();
            e.Handled = true;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // TODO: Implement search functionality
        var searchText = SearchBox.Text;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            System.Diagnostics.Debug.WriteLine($"Searching: {searchText}");
        }
    }

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        // Show power options context menu
        var contextMenu = new ContextMenu();

        var shutdownItem = new MenuItem { Header = "Kapat" };
        shutdownItem.Click += (s, args) => ShutdownComputer();

        var restartItem = new MenuItem { Header = "Yeniden Başlat" };
        restartItem.Click += (s, args) => RestartComputer();

        var sleepItem = new MenuItem { Header = "Uyku" };
        sleepItem.Click += (s, args) => SleepComputer();

        var signOutItem = new MenuItem { Header = "Oturumu Kapat" };
        signOutItem.Click += (s, args) => SignOut();

        contextMenu.Items.Add(sleepItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(shutdownItem);
        contextMenu.Items.Add(restartItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(signOutItem);

        contextMenu.IsOpen = true;
    }

    #region Power Actions

    [DllImport("user32.dll")]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("PowrProf.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private const uint EWX_SHUTDOWN = 0x00000001;
    private const uint EWX_REBOOT = 0x00000002;
    private const uint EWX_LOGOFF = 0x00000000;
    private const uint EWX_FORCE = 0x00000004;

    private void ShutdownComputer()
    {
        HideMenu();
        System.Diagnostics.Process.Start("shutdown", "/s /t 0");
    }

    private void RestartComputer()
    {
        HideMenu();
        System.Diagnostics.Process.Start("shutdown", "/r /t 0");
    }

    private void SleepComputer()
    {
        HideMenu();
        SetSuspendState(false, false, false);
    }

    private void SignOut()
    {
        HideMenu();
        ExitWindowsEx(EWX_LOGOFF, 0);
    }

    #endregion

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Don't actually close, just hide
        if (!App.Instance.IsMenuVisible)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        HideMenu();
    }
}
