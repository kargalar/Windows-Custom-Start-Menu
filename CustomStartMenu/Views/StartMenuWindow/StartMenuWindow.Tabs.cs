using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomStartMenu.Models;

namespace CustomStartMenu.Views;

/// <summary>
/// Tab management functionality for the Start Menu
/// </summary>
public partial class StartMenuWindow
{
    private void RefreshTabs()
    {
        TabsPanel.Children.Clear();

        foreach (var tab in _pinnedItemsService.Tabs.OrderBy(t => t.Order))
        {
            var tabButton = CreateTabButton(tab);
            TabsPanel.Children.Add(tabButton);
        }
    }

    private Button CreateTabButton(Tab tab)
    {
        var button = new Button
        {
            Style = (Style)FindResource("TabButtonStyle"),
            Content = new TextBlock 
            { 
                Text = tab.Name, 
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 100
            },
            Tag = tab.Id == _currentTabId ? "Active" : tab.Id,
            AllowDrop = true
        };

        button.Click += (s, e) =>
        {
            _currentTabId = tab.Id;
            ResetPageOnTabChange();
            RefreshTabs();
            RefreshPinnedItems();
        };

        button.MouseRightButtonUp += (s, e) =>
        {
            ShowTabContextMenu(tab, button);
            e.Handled = true;
        };

        // Drag & drop support for tabs
        button.Drop += (s, e) =>
        {
            if (_draggedItem != null)
            {
                var gridColumns = CalculateGridColumns();
                _pinnedItemsService.MoveItemToTab(_draggedItem.Id, tab.Id, gridColumns);
                _currentTabId = tab.Id;
                RefreshTabs();
                RefreshPinnedItems();
            }
            e.Handled = true;
        };

        button.DragOver += (s, e) =>
        {
            if (_draggedItem != null)
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        };

        return button;
    }

    private void ShowTabContextMenu(Tab tab, Button button)
    {
        var contextMenu = new ContextMenu();

        var renameItem = new MenuItem { Header = "Yeniden Adlandır" };
        renameItem.Click += (s, e) => StartInlineTabEdit(tab, button);

        var deleteItem = new MenuItem { Header = "Sekmeyi Sil" };
        deleteItem.Click += (s, e) =>
        {
            if (_pinnedItemsService.Tabs.Count > 1)
            {
                var result = MessageBox.Show(
                    $"'{tab.Name}' sekmesini silmek istediğinizden emin misiniz?\nİçindeki öğeler varsayılan sekmeye taşınacak.",
                    "Sekmeyi Sil",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    if (_currentTabId == tab.Id)
                    {
                        _currentTabId = _pinnedItemsService.Tabs.First(t => t.Id != tab.Id).Id;
                    }
                    _pinnedItemsService.RemoveTab(tab.Id);
                }
            }
            else
            {
                MessageBox.Show("En az bir sekme olmalıdır.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        contextMenu.Items.Add(renameItem);
        if (_pinnedItemsService.Tabs.Count > 1)
        {
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(deleteItem);
        }

        contextMenu.PlacementTarget = button;
        contextMenu.IsOpen = true;
    }

    private void StartInlineTabEdit(Tab tab, Button tabButton)
    {
        // Cancel any pinned-item inline rename first
        CancelInlineRename();
        
        // Cancel any existing tab edit
        CancelInlineTabEdit();
        
        if (TabBarPanel.Visibility != Visibility.Visible)
        {
            return;
        }
        
        if (tabButton.Content is not TextBlock)
        {
            return;
        }
        
        var textBox = new TextBox
        {
            Text = tab.Name,
            MinWidth = 80,
            MaxWidth = 150,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            CaretBrush = (Brush)FindResource("TextBrush"),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        
        _activeTabEditTextBox = textBox;
        _activeTabEditButton = tabButton;
        _activeTabEditTabId = tab.Id;
        
        tabButton.Content = textBox;
        textBox.SelectAll();
        textBox.Focus();
        
        textBox.KeyDown += TabEditTextBox_KeyDown;
        textBox.LostFocus += TabEditTextBox_LostFocus;
    }
    
    private void TabEditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitInlineTabEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelInlineTabEdit();
            e.Handled = true;
        }
    }
    
    private void TabEditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitInlineTabEdit();
    }
    
    private void CommitInlineTabEdit()
    {
        if (_activeTabEditTextBox == null)
        {
            return;
        }
        
        var newName = _activeTabEditTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(newName) && !string.IsNullOrWhiteSpace(_activeTabEditTabId))
        {
            _pinnedItemsService.RenameTab(_activeTabEditTabId, newName);
        }
        
        CancelInlineTabEdit(refresh: true);
    }
    
    private void CancelInlineTabEdit(bool refresh = false)
    {
        if (_activeTabEditTextBox != null)
        {
            _activeTabEditTextBox.KeyDown -= TabEditTextBox_KeyDown;
            _activeTabEditTextBox.LostFocus -= TabEditTextBox_LostFocus;
        }
        
        _activeTabEditTextBox = null;
        _activeTabEditButton = null;
        _activeTabEditTabId = null;
        
        if (refresh)
        {
            RefreshTabs();
            RefreshPinnedItems();
        }
        else
        {
            RefreshTabs();
        }
    }

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        // Cancel any existing tab edit
        CancelInlineTabEdit();
        
        // Create inline TextBox for new tab name
        var textBox = new TextBox
        {
            Text = "Yeni Sekme",
            MinWidth = 80,
            MaxWidth = 150,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            CaretBrush = (Brush)FindResource("TextBrush"),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        
        // Add to tabs panel
        TabsPanel.Children.Add(textBox);
        _activeTabEditTextBox = textBox;
        textBox.SelectAll();
        textBox.Focus();
        
        // Handle Enter key to confirm
        textBox.KeyDown += (s, args) =>
        {
            if (args.Key == Key.Enter)
            {
                var name = textBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var newTab = _pinnedItemsService.AddTab(name);
                    _currentTabId = newTab.Id;
                }
                TabsPanel.Children.Remove(textBox);
                _activeTabEditTextBox = null;
                RefreshTabs();
                RefreshPinnedItems();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                TabsPanel.Children.Remove(textBox);
                _activeTabEditTextBox = null;
                args.Handled = true;
            }
        };
        
        // Handle lost focus to confirm or cancel
        textBox.LostFocus += (s, args) =>
        {
            if (TabsPanel.Children.Contains(textBox))
            {
                var name = textBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var newTab = _pinnedItemsService.AddTab(name);
                    _currentTabId = newTab.Id;
                }
                TabsPanel.Children.Remove(textBox);
                _activeTabEditTextBox = null;
                RefreshTabs();
                RefreshPinnedItems();
            }
        };
    }
}
