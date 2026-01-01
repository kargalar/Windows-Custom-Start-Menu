using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CustomStartMenu.Models;

namespace CustomStartMenu.Views;

/// <summary>
/// Power actions (shutdown, restart, sleep, sign out) for the Start Menu
/// </summary>
public partial class StartMenuWindow
{
    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
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

    private void ShutdownComputer()
    {
        HideMenu();
        Process.Start("shutdown", "/s /t 0");
    }

    private void RestartComputer()
    {
        HideMenu();
        Process.Start("shutdown", "/r /t 0");
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
}
