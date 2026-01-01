using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CustomStartMenu.Models;
using CustomStartMenu.Services;
using System.Diagnostics;

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
    private readonly SearchService _searchService;
    private readonly PinnedItemsService _pinnedItemsService;
    private readonly IconService _iconService;
    private CancellationTokenSource? _searchCts;
    private bool _isInSearchMode;
    
    // Tab management
    private string? _currentTabId;
    private string? _openGroupId; // Currently open group (folder view)
    
    // Drag & Drop
    private Point _dragStartPoint;
    private bool _isDragging;
    private PinnedItem? _draggedItem;
    private Button? _draggedButton;

    public StartMenuWindow()
    {
        InitializeComponent();
        SetUserName();

        _searchService = new SearchService();
        _pinnedItemsService = App.Instance.PinnedItemsService;
        _iconService = IconService.Instance;
        _pinnedItemsService.PinnedItemsChanged += OnPinnedItemsChanged;

        // Set initial tab
        _currentTabId = _pinnedItemsService.DefaultTab.Id;

        RefreshTabs();
        RefreshPinnedItems();
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
                Left = workArea.Left + 12;
                Top = workArea.Bottom - Height;
                break;

            case ABE_TOP:
                Left = workArea.Left + 12;
                Top = workArea.Top;
                break;

            case ABE_LEFT:
                Left = workArea.Left;
                Top = workArea.Bottom - Height;
                break;

            case ABE_RIGHT:
                Left = workArea.Right - Width;
                Top = workArea.Bottom - Height;
                break;

            default:
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

        // Reset to pinned items view
        SwitchToPinnedView();

        // Reset opacity for animation
        MainBorder.Opacity = 0;
        MainBorder.RenderTransform = new TranslateTransform(0, 20);

        Show();
        Activate();

        // Animate in
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        var slideIn = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        MainBorder.BeginAnimation(OpacityProperty, fadeIn);
        ((TranslateTransform)MainBorder.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty, slideIn);

        RefreshTabs();
        RefreshPinnedItems();
    }

    public void HideMenu()
    {
        if (!IsVisible || _isClosing) return;

        _isClosing = true;
        _searchCts?.Cancel();

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
        HideMenu();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isInSearchMode)
            {
                SwitchToPinnedView();
                e.Handled = true;
            }
            else
            {
                HideMenu();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.LWin || e.Key == Key.RWin)
        {
            HideMenu();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && _isInSearchMode && string.IsNullOrEmpty(SearchBox.Text))
        {
            SwitchToPinnedView();
            e.Handled = true;
        }
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Any text input switches to search mode
        if (!_isInSearchMode && !string.IsNullOrWhiteSpace(e.Text))
        {
            SwitchToSearchView();
            SearchBox.Text = e.Text;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Switch tabs with mouse wheel when not in search mode
        if (!_isInSearchMode && !IsMouseOverScrollableContent())
        {
            var tabs = _pinnedItemsService.Tabs.OrderBy(t => t.Order).ToList();
            var currentIndex = tabs.FindIndex(t => t.Id == _currentTabId);
            
            if (e.Delta > 0 && currentIndex > 0)
            {
                // Scroll up = previous tab
                _currentTabId = tabs[currentIndex - 1].Id;
                RefreshTabs();
                RefreshPinnedItems();
                e.Handled = true;
            }
            else if (e.Delta < 0 && currentIndex < tabs.Count - 1)
            {
                // Scroll down = next tab
                _currentTabId = tabs[currentIndex + 1].Id;
                RefreshTabs();
                RefreshPinnedItems();
                e.Handled = true;
            }
        }
    }

    private bool IsMouseOverScrollableContent()
    {
        var mousePos = Mouse.GetPosition(this);
        
        // Check if mouse is over the pinned items scroll viewer
        if (PinnedScrollViewer.IsVisible)
        {
            var scrollViewerPos = PinnedScrollViewer.TranslatePoint(new Point(0, 0), this);
            var scrollViewerRect = new Rect(scrollViewerPos, new Size(PinnedScrollViewer.ActualWidth, PinnedScrollViewer.ActualHeight));
            
            if (scrollViewerRect.Contains(mousePos))
            {
                // Only handle if content is scrollable
                return PinnedScrollViewer.ScrollableHeight > 0;
            }
        }
        
        return false;
    }

    private void SwitchToSearchView()
    {
        _isInSearchMode = true;
        PinnedItemsView.Visibility = Visibility.Collapsed;
        SearchView.Visibility = Visibility.Visible;
        SearchBox.Clear();
        SearchBox.Focus();
        SearchResultsPanel.Items.Clear();
        SearchingText.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
    }

    private void SwitchToPinnedView()
    {
        _isInSearchMode = false;
        SearchView.Visibility = Visibility.Collapsed;
        PinnedItemsView.Visibility = Visibility.Visible;
        _searchCts?.Cancel();
    }

    #region Tab Management

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
                _pinnedItemsService.MoveItemToTab(_draggedItem.Id, tab.Id);
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
        renameItem.Click += (s, e) => ShowRenameTabDialog(tab);

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

    private void ShowRenameTabDialog(Tab tab)
    {
        var dialog = new Window
        {
            Title = "Sekmeyi Yeniden Adlandır",
            Width = 300,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        var textBox = new TextBox { Text = tab.Name, Margin = new Thickness(0, 0, 0, 12) };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        
        var okButton = new Button { Content = "Tamam", Width = 75, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "İptal", Width = 75 };

        okButton.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                _pinnedItemsService.RenameTab(tab.Id, textBox.Text);
                dialog.Close();
            }
        };

        cancelButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        textBox.SelectAll();
        textBox.Focus();

        dialog.ShowDialog();
    }

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Yeni Sekme",
            Width = 300,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        var textBox = new TextBox { Text = "Yeni Sekme", Margin = new Thickness(0, 0, 0, 12) };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        
        var okButton = new Button { Content = "Ekle", Width = 75, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "İptal", Width = 75 };

        okButton.Click += (s, args) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                var newTab = _pinnedItemsService.AddTab(textBox.Text);
                _currentTabId = newTab.Id;
                RefreshTabs();
                RefreshPinnedItems();
                dialog.Close();
            }
        };

        cancelButton.Click += (s, args) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        textBox.SelectAll();
        textBox.Focus();

        dialog.ShowDialog();
    }

    #endregion

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;

        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResultsPanel.Items.Clear();
            SearchingText.Visibility = Visibility.Collapsed;
            NoResultsText.Visibility = Visibility.Collapsed;
            return;
        }

        // Cancel previous search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        SearchingText.Visibility = Visibility.Visible;
        NoResultsText.Visibility = Visibility.Collapsed;

        try
        {
            // Small delay for debouncing
            await Task.Delay(150, token);

            var results = await _searchService.SearchAsync(query, token);

            if (token.IsCancellationRequested) return;

            SearchingText.Visibility = Visibility.Collapsed;
            SearchResultsPanel.Items.Clear();

            if (results.Count == 0)
            {
                NoResultsText.Visibility = Visibility.Visible;
                return;
            }

            foreach (var result in results)
            {
                var button = CreateSearchResultButton(result);
                SearchResultsPanel.Items.Add(button);
            }
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Search error: {ex.Message}");
            SearchingText.Visibility = Visibility.Collapsed;
        }
    }

    private Button CreateSearchResultButton(SearchResult result)
    {
        // Get real icon from IconService
        var icon = _iconService.GetIcon(result.Path);

        var iconElement = icon != null
            ? (UIElement)new Image 
            { 
                Source = icon, 
                Width = 32, 
                Height = 32, 
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            }
            : (UIElement)new TextBlock 
            { 
                Text = GetFallbackIcon(result.Type), 
                FontSize = 24, 
                Margin = new Thickness(0, 0, 12, 0), 
                VerticalAlignment = VerticalAlignment.Center 
            };

        var button = new Button
        {
            Style = (Style)FindResource("SearchResultStyle"),
            Tag = result.Path,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    iconElement,
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = result.Name, FontSize = 14, Foreground = (Brush)FindResource("TextBrush") },
                            new TextBlock { Text = result.Path, FontSize = 11, Foreground = (Brush)FindResource("TextSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 450 }
                        }
                    }
                }
            }
        };

        button.Click += SearchResult_Click;
        return button;
    }

    private static string GetFallbackIcon(SearchResultType type)
    {
        return type switch
        {
            SearchResultType.Application => "📦",
            SearchResultType.Folder => "📁",
            SearchResultType.File => "📄",
            _ => "📄"
        };
    }

    private void SearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            LaunchItem(path);
        }
    }

    #region Pinned Items

    private void OnPinnedItemsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RefreshTabs();
            RefreshPinnedItems();
        });
    }

    public void RefreshPinnedItems()
    {
        PinnedItemsContainer.Children.Clear();

        var currentTabId = _currentTabId ?? _pinnedItemsService.DefaultTab.Id;
        
        // Check if we're inside a group (folder view)
        if (_openGroupId != null)
        {
            var group = _pinnedItemsService.Groups.FirstOrDefault(g => g.Id == _openGroupId);
            if (group != null)
            {
                // Show group content view
                ShowGroupContent(group, currentTabId);
                return;
            }
            else
            {
                _openGroupId = null; // Group was deleted
            }
        }

        var groups = _pinnedItemsService.GetGroupsForTab(currentTabId).ToList();
        var ungroupedItems = _pinnedItemsService.GetUngroupedItemsForTab(currentTabId).ToList();
        var totalItems = ungroupedItems.Count + groups.Count;

        if (totalItems == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        // Create a single WrapPanel for both items and group folders
        var mainWrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            AllowDrop = true
        };
        mainWrapPanel.Drop += WrapPanel_Drop;
        mainWrapPanel.DragOver += WrapPanel_DragOver;

        // Add ungrouped items
        foreach (var item in ungroupedItems.OrderBy(i => i.Order))
        {
            var button = CreatePinnedItemButton(item);
            mainWrapPanel.Children.Add(button);
        }

        // Add group folders (same size as pinned items)
        foreach (var group in groups.OrderBy(g => g.Order))
        {
            var groupButton = CreateGroupFolderButton(group, currentTabId);
            mainWrapPanel.Children.Add(groupButton);
        }

        PinnedItemsContainer.Children.Add(mainWrapPanel);
    }

    private void ShowGroupContent(Group group, string tabId)
    {
        EmptyState.Visibility = Visibility.Collapsed;

        var contentPanel = new StackPanel();

        // Back button and group name header
        var headerPanel = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            Margin = new Thickness(0, 0, 0, 12) 
        };
        
        var backButton = new Button
        {
            Content = "← Geri",
            Style = (Style)FindResource("MenuButtonStyle"),
            Margin = new Thickness(0, 0, 12, 0)
        };
        backButton.Click += (s, e) =>
        {
            _openGroupId = null;
            RefreshPinnedItems();
        };

        var groupTitle = new TextBlock
        {
            Text = group.Name,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };

        headerPanel.Children.Add(backButton);
        headerPanel.Children.Add(groupTitle);
        contentPanel.Children.Add(headerPanel);

        // Group items
        var items = _pinnedItemsService.GetItemsForTab(tabId, group.Id).ToList();
        
        if (items.Any())
        {
            var wrapPanel = CreateItemsWrapPanel(items, group.Id);
            contentPanel.Children.Add(wrapPanel);
        }
        else
        {
            var emptyText = new TextBlock
            {
                Text = "Bu grup boş. Öğeleri buraya sürükleyebilirsiniz.",
                FontSize = 13,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(8, 16, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            contentPanel.Children.Add(emptyText);
        }

        PinnedItemsContainer.Children.Add(contentPanel);
    }

    private Button CreateGroupFolderButton(Group group, string tabId)
    {
        var itemCount = _pinnedItemsService.GetItemsForTab(tabId, group.Id).Count();
        
        // Create folder icon with item count preview
        var folderContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Folder visual - show mini previews of first items
        var folderVisual = new Grid
        {
            Width = 48,
            Height = 48,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // Folder background
        var folderBg = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 200, 100)),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 200, 100)),
            BorderThickness = new Thickness(2)
        };
        folderVisual.Children.Add(folderBg);

        // Folder icon
        var folderIcon = new TextBlock
        {
            Text = "📁",
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        folderVisual.Children.Add(folderIcon);

        // Item count badge
        if (itemCount > 0)
        {
            var badge = new Border
            {
                Background = (Brush)FindResource("AccentBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 2, 5, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4)
            };
            badge.Child = new TextBlock
            {
                Text = itemCount.ToString(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            folderVisual.Children.Add(badge);
        }

        folderContent.Children.Add(folderVisual);

        // Group name
        folderContent.Children.Add(new TextBlock
        {
            Text = group.Name,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });

        var button = new Button
        {
            Style = (Style)FindResource("PinnedItemStyle"),
            Tag = group,
            ToolTip = $"{group.Name} ({itemCount} öğe)",
            Content = folderContent,
            AllowDrop = true
        };

        // Click to open folder
        button.Click += (s, e) =>
        {
            _openGroupId = group.Id;
            RefreshPinnedItems();
        };

        // Right-click context menu
        button.MouseRightButtonUp += (s, e) =>
        {
            ShowGroupContextMenu(group, button);
            e.Handled = true;
        };

        // Drop onto folder
        button.Drop += GroupFolder_Drop;
        button.DragOver += GroupFolder_DragOver;
        button.DragEnter += GroupFolder_DragEnter;
        button.DragLeave += GroupFolder_DragLeave;

        return button;
    }

    private void GroupFolder_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem"))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void GroupFolder_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem") && sender is Button button)
        {
            // Highlight folder when dragging over
            button.Opacity = 0.7;
            
            // Scale up slightly to indicate drop target
            button.RenderTransform = new ScaleTransform(1.1, 1.1);
            button.RenderTransformOrigin = new Point(0.5, 0.5);
        }
    }

    private void GroupFolder_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            // Remove highlight
            button.Opacity = 1.0;
            button.RenderTransform = null;
        }
    }

    private void GroupFolder_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            // Remove highlight
            button.Opacity = 1.0;
            button.RenderTransform = null;
        }

        if (e.Data.GetDataPresent("PinnedItem") && sender is Button btn && btn.Tag is Group group)
        {
            var droppedItem = e.Data.GetData("PinnedItem") as PinnedItem;
            if (droppedItem != null && droppedItem.GroupId != group.Id)
            {
                _pinnedItemsService.MoveItemToGroup(droppedItem.Id, group.Id);
            }
        }
        e.Handled = true;
    }

    private WrapPanel CreateItemsWrapPanel(IEnumerable<PinnedItem> items, string? groupId)
    {
        var wrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Tag = groupId // For drag & drop
        };

        wrapPanel.AllowDrop = true;
        wrapPanel.Drop += WrapPanel_Drop;
        wrapPanel.DragOver += WrapPanel_DragOver;

        foreach (var item in items.OrderBy(i => i.Order))
        {
            var button = CreatePinnedItemButton(item);
            wrapPanel.Children.Add(button);
        }

        return wrapPanel;
    }

    private void ShowGroupContextMenu(Group group, UIElement target)
    {
        var contextMenu = new ContextMenu();

        var renameItem = new MenuItem { Header = "Grubu Yeniden Adlandır" };
        renameItem.Click += (s, e) => ShowRenameGroupDialog(group);

        var deleteItem = new MenuItem { Header = "Grubu Sil" };
        deleteItem.Click += (s, e) =>
        {
            var result = MessageBox.Show(
                $"'{group.Name}' grubunu silmek istediğinizden emin misiniz?\nİçindeki öğeler grupsuz olacak.",
                "Grubu Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _pinnedItemsService.RemoveGroup(group.Id);
            }
        };

        contextMenu.Items.Add(renameItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(deleteItem);

        contextMenu.PlacementTarget = target;
        contextMenu.IsOpen = true;
    }

    private void ShowRenameGroupDialog(Group group)
    {
        var dialog = new Window
        {
            Title = "Grubu Yeniden Adlandır",
            Width = 300,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        var textBox = new TextBox { Text = group.Name, Margin = new Thickness(0, 0, 0, 12) };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        
        var okButton = new Button { Content = "Tamam", Width = 75, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "İptal", Width = 75 };

        okButton.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                _pinnedItemsService.RenameGroup(group.Id, textBox.Text);
                dialog.Close();
            }
        };

        cancelButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        textBox.SelectAll();
        textBox.Focus();

        dialog.ShowDialog();
    }

    private Button CreatePinnedItemButton(PinnedItem item)
    {
        // Get real icon from IconService
        var icon = _iconService.GetIcon(item.Path);

        var iconElement = icon != null
            ? (UIElement)new Image 
            { 
                Source = icon, 
                Width = 40, 
                Height = 40, 
                HorizontalAlignment = HorizontalAlignment.Center, 
                Margin = new Thickness(0, 0, 0, 8) 
            }
            : (UIElement)new TextBlock 
            { 
                Text = item.Type == PinnedItemType.Folder ? "📁" : "📦", 
                FontSize = 32, 
                HorizontalAlignment = HorizontalAlignment.Center, 
                Margin = new Thickness(0, 0, 0, 8) 
            };

        var button = new Button
        {
            Style = (Style)FindResource("PinnedItemStyle"),
            Tag = item,
            ToolTip = item.Path,
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    iconElement,
                    new TextBlock 
                    { 
                        Text = item.Name, 
                        FontSize = 12, 
                        Foreground = (Brush)FindResource("TextBrush"), 
                        TextTrimming = TextTrimming.CharacterEllipsis, 
                        MaxWidth = 80, 
                        HorizontalAlignment = HorizontalAlignment.Center, 
                        TextAlignment = TextAlignment.Center 
                    }
                }
            }
        };

        button.Click += PinnedItem_Click;
        button.MouseRightButtonUp += PinnedItem_RightClick;
        
        // Drag & Drop events
        button.PreviewMouseLeftButtonDown += PinnedItem_PreviewMouseLeftButtonDown;
        button.PreviewMouseMove += PinnedItem_PreviewMouseMove;
        button.PreviewMouseLeftButtonUp += PinnedItem_PreviewMouseLeftButtonUp;

        return button;
    }

    #region Drag & Drop

    private void PinnedItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
        
        if (sender is Button button && button.Tag is PinnedItem item)
        {
            _draggedButton = button;
            _draggedItem = item;
        }
    }

    private void PinnedItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null || _draggedButton == null)
            return;

        var currentPos = e.GetPosition(null);
        var diff = _dragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragging = true;
            
            // Dim original button during drag
            _draggedButton.Opacity = 0.3;
            
            var data = new DataObject("PinnedItem", _draggedItem);
            DragDrop.DoDragDrop(_draggedButton, data, DragDropEffects.Move);
            
            // Restore opacity after drag
            if (_draggedButton != null)
                _draggedButton.Opacity = 1.0;
            
            _isDragging = false;
            _draggedItem = null;
            _draggedButton = null;
        }
    }

    private void PinnedItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedButton != null)
            _draggedButton.Opacity = 1.0;
            
        _isDragging = false;
        _draggedItem = null;
        _draggedButton = null;
    }

    private void WrapPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem"))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void WrapPanel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem") && sender is WrapPanel wrapPanel)
        {
            var droppedItem = e.Data.GetData("PinnedItem") as PinnedItem;
            if (droppedItem == null) return;

            var groupId = wrapPanel.Tag as string;
            var dropPosition = e.GetPosition(wrapPanel);
            
            // Find drop index based on position
            var dropIndex = GetDropIndex(wrapPanel, dropPosition);

            // Move item
            if (droppedItem.GroupId != groupId)
            {
                _pinnedItemsService.MoveItemToGroup(droppedItem.Id, groupId);
            }
            
            _pinnedItemsService.MoveItem(droppedItem.Id, dropIndex, _currentTabId, groupId);
        }
        e.Handled = true;
    }

    private int GetDropIndex(WrapPanel wrapPanel, Point dropPosition)
    {
        for (int i = 0; i < wrapPanel.Children.Count; i++)
        {
            var child = wrapPanel.Children[i];
            
            // Skip group folder buttons for index calculation 
            if (child is Button btn && btn.Tag is Group)
                continue;
                
            var childPos = child.TranslatePoint(new Point(0, 0), wrapPanel);
            var childRect = new Rect(childPos, new Size(((FrameworkElement)child).ActualWidth, ((FrameworkElement)child).ActualHeight));
            
            if (dropPosition.X < childRect.Left + childRect.Width / 2)
            {
                return i;
            }
        }
        return wrapPanel.Children.Count;
    }

    private void PinnedScrollViewer_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem"))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void PinnedScrollViewer_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem"))
        {
            var droppedItem = e.Data.GetData("PinnedItem") as PinnedItem;
            if (droppedItem == null) return;

            // Move to ungrouped
            _pinnedItemsService.MoveItemToGroup(droppedItem.Id, null);
        }
        e.Handled = true;
    }

    private void PinnedScrollViewer_RightClick(object sender, MouseButtonEventArgs e)
    {
        // Check if click was on empty space (not on an item)
        var hitTest = VisualTreeHelper.HitTest(PinnedScrollViewer, e.GetPosition(PinnedScrollViewer));
        if (hitTest?.VisualHit is ScrollViewer || hitTest?.VisualHit is StackPanel || hitTest?.VisualHit is WrapPanel || hitTest?.VisualHit is Border)
        {
            // Check if the hit element has a PinnedItem tag
            var element = hitTest.VisualHit as FrameworkElement;
            while (element != null)
            {
                if (element is Button btn && btn.Tag is PinnedItem)
                {
                    return; // Don't show menu, let button handle it
                }
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            ShowEmptySpaceContextMenu(e.GetPosition(PinnedScrollViewer));
            e.Handled = true;
        }
    }

    private void ShowEmptySpaceContextMenu(Point position)
    {
        var contextMenu = new ContextMenu();

        var createGroupItem = new MenuItem { Header = "Yeni Grup Oluştur" };
        createGroupItem.Click += (s, e) => ShowCreateGroupDialog();

        contextMenu.Items.Add(createGroupItem);
        contextMenu.IsOpen = true;
    }

    private void ShowCreateGroupDialog()
    {
        var dialog = new Window
        {
            Title = "Yeni Grup",
            Width = 300,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        var textBox = new TextBox { Text = "Yeni Grup", Margin = new Thickness(0, 0, 0, 12) };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        
        var okButton = new Button { Content = "Oluştur", Width = 75, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "İptal", Width = 75 };

        okButton.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                _pinnedItemsService.AddGroup(textBox.Text, _currentTabId);
                dialog.Close();
            }
        };

        cancelButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        textBox.SelectAll();
        textBox.Focus();

        dialog.ShowDialog();
    }

    #endregion

    private void PinnedItem_Click(object sender, RoutedEventArgs e)
    {
        // Don't launch if we were dragging
        if (_isDragging) return;
        
        if (sender is Button button && button.Tag is PinnedItem item)
        {
            LaunchItem(item.Path);
        }
    }

    private void PinnedItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag is PinnedItem item)
        {
            var contextMenu = new ContextMenu();

            var openItem = new MenuItem { Header = "Aç" };
            openItem.Click += (s, args) => LaunchItem(item.Path);

            var openLocationItem = new MenuItem { Header = "Dosya konumunu aç" };
            openLocationItem.Click += (s, args) => OpenFileLocation(item.Path);

            // Move to group submenu
            var moveToGroupMenu = new MenuItem { Header = "Gruba Taşı" };
            
            var noGroupItem = new MenuItem { Header = "(Grupsuz)" };
            noGroupItem.Click += (s, args) => _pinnedItemsService.MoveItemToGroup(item.Id, null);
            moveToGroupMenu.Items.Add(noGroupItem);
            
            var groups = _pinnedItemsService.GetGroupsForTab(_currentTabId).ToList();
            if (groups.Any())
            {
                moveToGroupMenu.Items.Add(new Separator());
                foreach (var group in groups)
                {
                    var groupItem = new MenuItem { Header = group.Name };
                    groupItem.Click += (s, args) => _pinnedItemsService.MoveItemToGroup(item.Id, group.Id);
                    moveToGroupMenu.Items.Add(groupItem);
                }
            }

            // Move to tab submenu
            var moveToTabMenu = new MenuItem { Header = "Sekmeye Taşı" };
            foreach (var tab in _pinnedItemsService.Tabs.OrderBy(t => t.Order))
            {
                var tabItem = new MenuItem 
                { 
                    Header = tab.Name,
                    IsEnabled = tab.Id != _currentTabId
                };
                tabItem.Click += (s, args) =>
                {
                    _pinnedItemsService.MoveItemToTab(item.Id, tab.Id);
                };
                moveToTabMenu.Items.Add(tabItem);
            }

            var unpinItem = new MenuItem { Header = "Sabitlemeyi kaldır" };
            unpinItem.Click += (s, args) =>
            {
                _pinnedItemsService.RemovePinById(item.Id);
            };

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(openLocationItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(moveToGroupMenu);
            contextMenu.Items.Add(moveToTabMenu);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(unpinItem);

            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    #endregion

    #region Launch & Open

    private void LaunchItem(string path)
    {
        try
        {
            HideMenu();

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

    #endregion

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

    #region Power Actions

    [DllImport("user32.dll")]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("PowrProf.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private const uint EWX_LOGOFF = 0x00000000;

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

    #endregion

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!App.Instance.IsMenuVisible)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        HideMenu();
    }
}
