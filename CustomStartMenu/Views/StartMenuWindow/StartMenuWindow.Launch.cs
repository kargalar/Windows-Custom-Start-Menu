using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomStartMenu.Models;

namespace CustomStartMenu.Views;

/// <summary>
/// Launch and file operations for the Start Menu
/// </summary>
public partial class StartMenuWindow
{
    private void LaunchItem(string path)
    {
        try
        {
            HideMenu();

            // For .url files (internet shortcuts), extract the URL and open it directly
            if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path))
            {
                var url = ExtractUrlFromShortcut(path);
                if (!string.IsNullOrEmpty(url))
                {
                    var urlStartInfo = new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    };
                    Process.Start(urlStartInfo);
                    return;
                }
            }

            // Standard launch for other files
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch: {ex.Message}");
            MessageBox.Show($"Açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Extracts the URL from an internet shortcut (.url) file
    /// </summary>
    private string? ExtractUrlFromShortcut(string urlFilePath)
    {
        try
        {
            var lines = System.IO.File.ReadAllLines(urlFilePath);
            foreach (var line in lines)
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring("URL=".Length).Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to extract URL from shortcut: {ex.Message}");
        }
        return null;
    }

    private void OpenFileLocation(string path)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open location: {ex.Message}");
        }
    }

    private void StartInlineRename(Button button, PinnedItem item)
    {
        // Cancel any existing rename operation
        CancelInlineRename();
        
        // Find the TextBlock and TextBox within the button
        if (button.Content is StackPanel stackPanel)
        {
            foreach (var child in stackPanel.Children)
            {
                if (child is Grid nameContainer)
                {
                    TextBlock? textBlock = null;
                    TextBox? textBox = null;
                    
                    foreach (var gridChild in nameContainer.Children)
                    {
                        if (gridChild is TextBlock tb) textBlock = tb;
                        if (gridChild is TextBox tbx) textBox = tbx;
                    }
                    
                    if (textBlock != null && textBox != null)
                    {
                        _activeRenameTextBlock = textBlock;
                        _activeRenameTextBox = textBox;
                        _renamingItem = item;
                        
                        // Switch to edit mode
                        textBlock.Visibility = Visibility.Collapsed;
                        textBox.Text = item.DisplayName;
                        textBox.Visibility = Visibility.Visible;
                        textBox.SelectAll();
                        textBox.Focus();
                    }
                    break;
                }
            }
        }
    }
    
    private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        
        if (e.Key == Key.Enter)
        {
            CommitInlineRename(textBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelInlineRename();
            e.Handled = true;
        }
    }
    
    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && _activeRenameTextBox == textBox)
        {
            CommitInlineRename(textBox);
        }
    }
    
    private void CommitInlineRename(TextBox textBox)
    {
        if (textBox.Tag is object[] tagData && tagData.Length >= 2 && 
            tagData[0] is PinnedItem item && tagData[1] is TextBlock textBlock)
        {
            var newName = textBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(newName))
            {
                // If the new name is the same as the original file name, clear CustomName
                if (newName == item.Name)
                {
                    item.CustomName = null;
                }
                else
                {
                    item.CustomName = newName;
                }
                
                // Update the TextBlock with the new name
                textBlock.Text = item.DisplayName;
                _pinnedItemsService.UpdatePinnedItem(item);
            }
            
            // Switch back to display mode
            textBox.Visibility = Visibility.Collapsed;
            textBlock.Visibility = Visibility.Visible;
        }
        
        _activeRenameTextBox = null;
        _activeRenameTextBlock = null;
        _renamingItem = null;
    }
    
    private void CancelInlineRename()
    {
        if (_activeRenameTextBox != null && _activeRenameTextBlock != null)
        {
            _activeRenameTextBox.Visibility = Visibility.Collapsed;
            _activeRenameTextBlock.Visibility = Visibility.Visible;
        }
        
        _activeRenameTextBox = null;
        _activeRenameTextBlock = null;
        _renamingItem = null;
    }
}
