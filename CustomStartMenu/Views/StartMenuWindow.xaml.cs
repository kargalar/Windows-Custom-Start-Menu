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
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _searchCts;
    private bool _isInSearchMode;
    
    // Search keyboard navigation
    private int _selectedSearchIndex = -1; // -1 means no selection
    
    // Tab management
    private string? _currentTabId;
    private string? _openGroupId; // Currently open group (folder view)
    
    // Drag & Drop
    private Point _dragStartPoint;
    private bool _isDragging;
    private PinnedItem? _draggedItem;
    private Button? _draggedButton;
    
    // Group Drag & Drop
    private Group? _draggedGroup;
    private Button? _draggedGroupButton;
    
    // Drop indicator for visual feedback during drag
    private Border? _dropIndicator;
    private int _lastDropIndex = -1;
    
    // Mouse hook for click outside detection
    private readonly MouseHookService _mouseHookService;
    
    // Inline rename
    private TextBox? _activeRenameTextBox;
    private TextBlock? _activeRenameTextBlock;
    private PinnedItem? _renamingItem;

    // Inline tab edit (new tab name / rename tab)
    private TextBox? _activeTabEditTextBox;
    private Button? _activeTabEditButton;
    private string? _activeTabEditTabId;

    public StartMenuWindow()
    {
        InitializeComponent();

        _searchService = new SearchService();
        _pinnedItemsService = App.Instance.PinnedItemsService;
        _iconService = IconService.Instance;
        _settingsService = App.Instance.SettingsService;
        _mouseHookService = new MouseHookService();
        
        _pinnedItemsService.PinnedItemsChanged += OnPinnedItemsChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
        _mouseHookService.MouseClicked += OnMouseClickedOutside;
        App.Instance.KeyboardHookService.CharacterInput += OnGlobalCharacterInput;

        // Set initial tab
        _currentTabId = _pinnedItemsService.DefaultTab.Id;

        RefreshTabs();
        RefreshPinnedItems();
    }
    
    private void OnMouseClickedOutside(object? sender, MouseClickEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!IsVisible) return;
            
            // Get window bounds in screen coordinates
            var windowLeft = Left;
            var windowTop = Top;
            var windowRight = Left + ActualWidth;
            var windowBottom = Top + ActualHeight;
            
            // Check if click is outside the window
            if (e.X < windowLeft || e.X > windowRight || e.Y < windowTop || e.Y > windowBottom)
            {
                HideMenu();
            }
        });
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyTransparency();
            RefreshPinnedItems();
        });
    }

    /// <summary>
    /// Apply the current transparency setting to the MainBorder background
    /// </summary>
    private void ApplyTransparency()
    {
        var transparency = _settingsService.Settings.MenuTransparency;
        var darkness = _settingsService.Settings.BackgroundDarkness;
        
        // Create a new brush with the transparency and darkness applied
        var alpha = (byte)(transparency * 255);
        var gray = (byte)darkness;
        var backgroundColor = Color.FromArgb(alpha, gray, gray, gray);
        MainBorder.Background = new SolidColorBrush(backgroundColor);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        ApplyTransparency();
    }

    private void PositionWindow()
    {
        // Apply menu size first
        ApplyMenuSize();

        // Get taskbar position and size
        var taskbarInfo = GetTaskbarInfo();

        // Get working area (screen minus taskbar)
        var workArea = SystemParameters.WorkArea;

        // Check if position should be centered
        bool centerPosition = _settingsService.Settings.Position == MenuPosition.Center;

        // Position based on taskbar location and position setting
        switch (taskbarInfo.Edge)
        {
            case ABE_BOTTOM:
                if (centerPosition)
                    Left = workArea.Left + (workArea.Width - Width) / 2;
                else
                    Left = workArea.Left + 12;
                Top = workArea.Bottom - Height;
                break;

            case ABE_TOP:
                if (centerPosition)
                    Left = workArea.Left + (workArea.Width - Width) / 2;
                else
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
                if (centerPosition)
                    Left = workArea.Left + (workArea.Width - Width) / 2;
                else
                    Left = 12;
                Top = workArea.Bottom - Height;
                break;
        }
    }

    private void ApplyMenuSize()
    {
        var settings = _settingsService.Settings;
        var workArea = SystemParameters.WorkArea;

        switch (settings.Size)
        {
            case MenuSize.Small:
                Width = 500;
                Height = 600;
                WindowState = WindowState.Normal;
                break;

            case MenuSize.Normal:
                Width = 650;
                Height = 750;
                WindowState = WindowState.Normal;
                break;

            case MenuSize.Large:
                Width = 900;
                Height = 850;
                WindowState = WindowState.Normal;
                break;

            case MenuSize.VeryLarge:
                Width = 1100;
                Height = 950;
                WindowState = WindowState.Normal;
                break;

            case MenuSize.Fullscreen:
                Width = workArea.Width;
                Height = workArea.Height;
                Left = workArea.Left;
                Top = workArea.Top;
                WindowState = WindowState.Normal;
                break;

            case MenuSize.Custom:
                Width = Math.Min(settings.CustomWidth, workArea.Width);
                Height = Math.Min(settings.CustomHeight, workArea.Height);
                WindowState = WindowState.Normal;
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

        bool useAnimations = _settingsService.Settings.EnableAnimations;

        if (useAnimations)
        {
            // Reset opacity for animation
            MainBorder.Opacity = 0;
            MainBorder.RenderTransform = new TranslateTransform(0, 20);
        }
        else
        {
            MainBorder.Opacity = 1;
            MainBorder.RenderTransform = new TranslateTransform(0, 0);
        }

        Show();
        Activate();

        if (useAnimations)
        {
            // Animate in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            var slideIn = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            MainBorder.BeginAnimation(OpacityProperty, fadeIn);
            ((TranslateTransform)MainBorder.RenderTransform).BeginAnimation(
                TranslateTransform.YProperty, slideIn);
        }

        // Start mouse hook to detect clicks outside
        _mouseHookService.StartHook();
        
        // Enable global text input capture
        App.Instance.KeyboardHookService.CaptureTextInput = true;
        
        RefreshTabs();
        RefreshPinnedItems();
    }

    public void HideMenu()
    {
        if (!IsVisible || _isClosing) return;

        _isClosing = true;
        _searchCts?.Cancel();
        
        // Stop mouse hook
        _mouseHookService.StopHook();
        
        // Disable global text input capture
        App.Instance.KeyboardHookService.CaptureTextInput = false;

        // Cancel hotkey assignment if active
        if (_isAssigningHotkey)
        {
            CancelHotkeyAssignment();
        }

        bool useAnimations = _settingsService.Settings.EnableAnimations;

        if (useAnimations)
        {
            // Animate out
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
            fadeOut.Completed += (s, e) =>
            {
                Hide();
                _isClosing = false;
            };

            MainBorder.BeginAnimation(OpacityProperty, fadeOut);
        }
        else
        {
            Hide();
            _isClosing = false;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        HideMenu();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handle hotkey assignment mode first
        if (_isAssigningHotkey)
        {
            HandleHotkeyAssignmentKeyDown(e);
            return;
        }

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
        // Don't switch to search mode if assigning hotkey
        if (_isAssigningHotkey)
        {
            e.Handled = true;
            return;
        }
        
        // Don't switch to search mode while typing into any TextBox (e.g., inline rename/new tab name)
        if (FocusManager.GetFocusedElement(this) is TextBox)
        {
            return;
        }
        
        // Don't switch to search mode if inline rename is active
        if (_activeRenameTextBox != null)
        {
            return;
        }
        
        // Don't switch to search mode if settings view is visible
        if (SettingsView.Visibility == Visibility.Visible)
        {
            return;
        }

        // Any text input switches to search mode
        if (!_isInSearchMode && !string.IsNullOrWhiteSpace(e.Text))
        {
            SwitchToSearchView();
            SearchBox.Text = e.Text;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
        }
    }
    
    private void OnGlobalCharacterInput(object? sender, char character)
    {
        Dispatcher.Invoke(() =>
        {
            if (!IsVisible) return;
            
            // Don't switch to search mode if assigning hotkey
            if (_isAssigningHotkey) return;
            
            // Don't switch to search mode if inline rename is active
            if (_activeRenameTextBox != null) return;
            
            // Don't switch to search mode if settings view is visible
            if (SettingsView.Visibility == Visibility.Visible) return;
            
            // Switch to search mode and add character
            if (!_isInSearchMode)
            {
                SwitchToSearchView();
                SearchBox.Text = character.ToString();
                SearchBox.CaretIndex = SearchBox.Text.Length;
            }
            else
            {
                // Already in search mode, append character
                SearchBox.Text += character;
                SearchBox.CaretIndex = SearchBox.Text.Length;
            }
            
            // Bring window to front and focus
            Activate();
            SearchBox.Focus();
        });
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Switch tabs with mouse wheel when not in search mode and not in settings view
        if (!_isInSearchMode && !IsMouseOverScrollableContent() && SettingsView.Visibility != Visibility.Visible)
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
        _selectedSearchIndex = -1; // Reset selection
        PinnedItemsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        SearchView.Visibility = Visibility.Visible;
        SearchBox.Clear();
        SearchBox.Focus();
        SearchResultsPanel.Items.Clear();
        SearchingText.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        
        // Hide tabs and show close button header with Arama title
        TabBarPanel.Visibility = Visibility.Collapsed;
        CloseButtonHeader.Visibility = Visibility.Visible;
        ViewTitleText.Text = "Arama";
    }

    private void SwitchToPinnedView()
    {
        _isInSearchMode = false;
        
        // Cancel hotkey assignment if active
        if (_isAssigningHotkey)
        {
            CancelHotkeyAssignment();
        }
        
        SearchView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PinnedItemsView.Visibility = Visibility.Visible;
        _searchCts?.Cancel();
        
        // Show tabs and hide close button header
        TabBarPanel.Visibility = Visibility.Visible;
        CloseButtonHeader.Visibility = Visibility.Collapsed;
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

            if (token.IsCancellationRequested) return;

            SearchingText.Visibility = Visibility.Collapsed;
            SearchResultsPanel.Items.Clear();

            // Reset selection when search changes
            _selectedSearchIndex = -1;

            // Check if query is a math expression first
            if (MathEvaluator.TryEvaluate(query, out var mathResult))
            {
                var formattedResult = MathEvaluator.FormatResult(mathResult);
                var calcResult = new SearchResult
                {
                    Name = $"{query} = {formattedResult}",
                    Path = formattedResult,
                    Type = SearchResultType.Calculation,
                    Score = 1000 // Highest score to ensure it's first
                };
                var calcButton = CreateSearchResultButton(calcResult);
                SearchResultsPanel.Items.Add(calcButton);
            }

            // Then search for applications
            var results = await _searchService.SearchAsync(query, token);

            if (token.IsCancellationRequested) return;

            if (results.Count == 0 && SearchResultsPanel.Items.Count == 0)
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

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var itemCount = SearchResultsPanel.Items.Count;

        if (e.Key == Key.Down)
        {
            // Move selection down
            if (itemCount > 0)
            {
                _selectedSearchIndex++;
                if (_selectedSearchIndex >= itemCount)
                {
                    _selectedSearchIndex = 0; // Wrap to first
                }
                UpdateSearchResultSelection();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_selectedSearchIndex == -1 && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                // No selection yet and user pressed Up - trigger web search
                OpenWebSearch(SearchBox.Text);
                e.Handled = true;
            }
            else if (itemCount > 0)
            {
                // Move selection up
                _selectedSearchIndex--;
                if (_selectedSearchIndex < 0)
                {
                    _selectedSearchIndex = itemCount - 1; // Wrap to last
                }
                UpdateSearchResultSelection();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter)
        {
            // Launch selected item or first result
            LaunchSelectedSearchResult();
            e.Handled = true;
        }
    }

    private void UpdateSearchResultSelection()
    {
        for (int i = 0; i < SearchResultsPanel.Items.Count; i++)
        {
            if (SearchResultsPanel.Items[i] is Button button)
            {
                if (i == _selectedSearchIndex)
                {
                    // Apply selected style
                    button.Style = (Style)FindResource("SearchResultSelectedStyle");
                }
                else
                {
                    // Apply normal style
                    button.Style = (Style)FindResource("SearchResultStyle");
                }
            }
        }
    }

    private void LaunchSelectedSearchResult()
    {
        if (SearchResultsPanel.Items.Count == 0) return;

        // If no selection, use first result
        var indexToLaunch = _selectedSearchIndex >= 0 ? _selectedSearchIndex : 0;

        if (indexToLaunch < SearchResultsPanel.Items.Count && 
            SearchResultsPanel.Items[indexToLaunch] is Button button)
        {
            // Simulate click on the button
            SearchResult_Click(button, new RoutedEventArgs());
        }
    }

    private void OpenWebSearch(string query)
    {
        try
        {
            var searchUrl = _settingsService.Settings.WebSearchUrl;
            var encodedQuery = Uri.EscapeDataString(query);
            var fullUrl = searchUrl + encodedQuery;

            HideMenu();

            var startInfo = new ProcessStartInfo
            {
                FileName = fullUrl,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open web search: {ex.Message}");
        }
    }

    private Button CreateSearchResultButton(SearchResult result)
    {
        // For calculation results, use emoji icon; for others, get real icon from IconService
        var icon = result.Type == SearchResultType.Calculation ? null : _iconService.GetIcon(result.Path);

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

        // For calculation results, show "Click to copy" hint
        var pathText = result.Type == SearchResultType.Calculation 
            ? "Sonucu panoya kopyalamak için tıklayın" 
            : result.Path;

        var button = new Button
        {
            Style = (Style)FindResource("SearchResultStyle"),
            Tag = result.Type == SearchResultType.Calculation ? result : result.Path,
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
                            new TextBlock { Text = pathText, FontSize = 11, Foreground = (Brush)FindResource("TextSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 450 }
                        }
                    }
                }
            }
        };

        button.Click += SearchResult_Click;
        
        // Add right-click handler for context menu (only for non-calculation results)
        if (result.Type != SearchResultType.Calculation)
        {
            button.MouseRightButtonUp += SearchResult_RightClick;
        }
        
        return button;
    }

    private void SearchResult_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            ShowSearchResultContextMenu(path, button);
            e.Handled = true;
        }
    }

    private void ShowSearchResultContextMenu(string path, UIElement target)
    {
        var contextMenu = new ContextMenu();

        // "Aç" (Open) menu item
        var openItem = new MenuItem { Header = "Aç" };
        openItem.Click += (s, args) => LaunchItem(path);
        contextMenu.Items.Add(openItem);

        // "Dosya konumunu aç" (Open file location) menu item
        var openLocationItem = new MenuItem { Header = "Dosya konumunu aç" };
        openLocationItem.Click += (s, args) => OpenFileLocation(path);
        contextMenu.Items.Add(openLocationItem);

        contextMenu.Items.Add(new Separator());

        // Check if item is already pinned
        var isPinned = _pinnedItemsService.IsPinned(path);

        if (isPinned)
        {
            // "Kaldır" (Remove/Unpin) menu item
            var unpinItem = new MenuItem { Header = "Kaldır" };
            unpinItem.Click += (s, args) =>
            {
                _pinnedItemsService.RemovePin(path);
            };
            contextMenu.Items.Add(unpinItem);
        }
        else
        {
            // "Pinle" (Pin) menu item
            var pinItem = new MenuItem { Header = "Pinle" };
            pinItem.Click += (s, args) =>
            {
                _pinnedItemsService.AddPin(path, _currentTabId);
            };
            contextMenu.Items.Add(pinItem);
        }

        contextMenu.PlacementTarget = target;
        contextMenu.IsOpen = true;
    }

    private static string GetFallbackIcon(SearchResultType type)
    {
        return type switch
        {
            SearchResultType.Application => "📦",
            SearchResultType.Folder => "📁",
            SearchResultType.File => "📄",
            SearchResultType.Calculation => "🔢",
            _ => "📄"
        };
    }

    private void SearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            // Check if this is a calculation result
            if (button.Tag is SearchResult result && result.Type == SearchResultType.Calculation)
            {
                // Copy the result to clipboard
                try
                {
                    Clipboard.SetText(result.Path); // Path contains the formatted result
                    
                    // Show brief feedback by changing button content temporarily
                    var originalContent = button.Content;
                    if (button.Content is StackPanel panel && panel.Children.Count > 1 && 
                        panel.Children[1] is StackPanel textPanel && textPanel.Children.Count > 0 &&
                        textPanel.Children[0] is TextBlock nameBlock)
                    {
                        var originalText = nameBlock.Text;
                        nameBlock.Text = "✓ Panoya kopyalandı!";
                        
                        // Restore after a short delay
                        Task.Delay(1000).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (nameBlock != null)
                                    nameBlock.Text = originalText;
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
                }
                return;
            }
            
            // Regular search result - launch the item
            if (button.Tag is string path)
            {
                LaunchItem(path);
            }
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

        // Check layout mode
        var layoutMode = _settingsService.Settings.PinnedItemsLayout;

        if (layoutMode == LayoutMode.FreeForm)
        {
            // FreeForm mode: Use Grid for positioning
            RenderFreeFormLayout(ungroupedItems, groups, currentTabId);
        }
        else
        {
            // Ordered mode: Use WrapPanel for auto-arrangement
            RenderOrderedLayout(ungroupedItems, groups, currentTabId);
        }
    }

    /// <summary>
    /// Render items in Ordered layout mode using WrapPanel (auto-arrangement)
    /// </summary>
    private void RenderOrderedLayout(List<PinnedItem> ungroupedItems, List<Group> groups, string currentTabId)
    {
        var showIconsOnly = _settingsService.Settings.ShowIconsOnly;
        var itemSize = _settingsService.Settings.ItemSize;
        
        // Get available width for items
        var availableWidth = PinnedScrollViewer.ActualWidth > 0 ? PinnedScrollViewer.ActualWidth - 16 : 600; // -16 for padding
        
        // Use ItemSize directly as button size
        double buttonSize = itemSize;
        double margin = 8.0; // 4px margin on each side
        double itemWidth = buttonSize + margin;
        
        // Calculate items per row based on item size
        var itemsPerRow = Math.Max(1, (int)(availableWidth / itemWidth));
        var totalWidth = itemsPerRow * itemWidth;

        // Create a single WrapPanel for both items and group folders
        var mainWrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            AllowDrop = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, // Center the items
            Width = totalWidth // Set fixed width for centering
        };

        mainWrapPanel.Drop += WrapPanel_Drop;
        mainWrapPanel.DragOver += WrapPanel_DragOver;

        // Get all elements sorted by GlobalOrder (mixed items and groups)
        var sortedElements = _pinnedItemsService.GetSortedElementsForTab(currentTabId);
        
        foreach (var element in sortedElements)
        {
            if (element is PinnedItem item)
            {
                var button = CreatePinnedItemButton(item, buttonSize);
                mainWrapPanel.Children.Add(button);
            }
            else if (element is Group group)
            {
                var groupButton = CreateGroupFolderButton(group, currentTabId, buttonSize);
                mainWrapPanel.Children.Add(groupButton);
            }
        }

        PinnedItemsContainer.Children.Add(mainWrapPanel);
    }

    /// <summary>
    /// Render items in FreeForm layout mode using Grid (user-positioned)
    /// </summary>
    private void RenderFreeFormLayout(List<PinnedItem> ungroupedItems, List<Group> groups, string currentTabId)
    {
        var itemSize = _settingsService.Settings.ItemSize;
        var cellSize = itemSize + 8; // Button size + margin
        
        // Calculate grid dimensions based on available space
        var availableWidth = PinnedScrollViewer.ActualWidth > 0 ? PinnedScrollViewer.ActualWidth : 600;
        var columns = Math.Max(1, (int)(availableWidth / cellSize));
        
        // Calculate required rows based on items with positions and items without
        var itemsWithPositions = ungroupedItems.Where(i => i.GridRow.HasValue && i.GridColumn.HasValue).ToList();
        var itemsWithoutPositions = ungroupedItems.Where(i => !i.GridRow.HasValue || !i.GridColumn.HasValue).ToList();
        
        var maxRow = itemsWithPositions.Any() ? itemsWithPositions.Max(i => i.GridRow!.Value) : -1;
        var totalItemsCount = ungroupedItems.Count + groups.Count;
        var minRows = (int)Math.Ceiling((double)totalItemsCount / columns);
        var rows = Math.Max(minRows, maxRow + 1) + 2; // Extra rows for expansion

        // Create the grid
        var grid = new Grid
        {
            AllowDrop = true,
            Background = Brushes.Transparent // Needed for drop events
        };
        grid.Drop += FreeFormGrid_Drop;
        grid.DragOver += FreeFormGrid_DragOver;

        // Define columns
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cellSize) });
        }

        // Define rows
        for (int r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellSize) });
        }

        // Track occupied cells
        var occupiedCells = new HashSet<(int row, int col)>();

        // Place items with positions first
        foreach (var item in itemsWithPositions)
        {
            var row = item.GridRow!.Value;
            var col = item.GridColumn!.Value;
            
            // Ensure within bounds
            if (row < rows && col < columns)
            {
                var button = CreatePinnedItemButton(item, itemSize);
                Grid.SetRow(button, row);
                Grid.SetColumn(button, col);
                grid.Children.Add(button);
                occupiedCells.Add((row, col));
            }
        }

        // Auto-place items without positions
        var nextRow = 0;
        var nextCol = 0;
        
        foreach (var item in itemsWithoutPositions.OrderBy(i => i.Order))
        {
            // Find next available cell
            while (occupiedCells.Contains((nextRow, nextCol)))
            {
                nextCol++;
                if (nextCol >= columns)
                {
                    nextCol = 0;
                    nextRow++;
                }
            }

            var button = CreatePinnedItemButton(item, itemSize);
            Grid.SetRow(button, nextRow);
            Grid.SetColumn(button, nextCol);
            grid.Children.Add(button);
            occupiedCells.Add((nextRow, nextCol));

            nextCol++;
            if (nextCol >= columns)
            {
                nextCol = 0;
                nextRow++;
            }
        }

        // Place groups
        foreach (var group in groups.OrderBy(g => g.Order))
        {
            // Find next available cell
            while (occupiedCells.Contains((nextRow, nextCol)))
            {
                nextCol++;
                if (nextCol >= columns)
                {
                    nextCol = 0;
                    nextRow++;
                }
            }

            var groupButton = CreateGroupFolderButton(group, currentTabId, itemSize);
            Grid.SetRow(groupButton, nextRow);
            Grid.SetColumn(groupButton, nextCol);
            grid.Children.Add(groupButton);
            occupiedCells.Add((nextRow, nextCol));

            nextCol++;
            if (nextCol >= columns)
            {
                nextCol = 0;
                nextRow++;
            }
        }

        PinnedItemsContainer.Children.Add(grid);
    }

    private void FreeFormGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem") || e.Data.GetDataPresent("Group"))
        {
            e.Effects = DragDropEffects.Move;
            
            if (sender is Grid grid)
            {
                var dropPosition = e.GetPosition(grid);
                var showIconsOnly = _settingsService.Settings.ShowIconsOnly;
                var cellSize = showIconsOnly ? 68 : 108;

                var gridRow = (int)(dropPosition.Y / cellSize);
                var gridCol = (int)(dropPosition.X / cellSize);

                // Ensure within bounds
                gridRow = Math.Max(0, Math.Min(gridRow, grid.RowDefinitions.Count - 1));
                gridCol = Math.Max(0, Math.Min(gridCol, grid.ColumnDefinitions.Count - 1));
                
                ShowFreeFormDropIndicator(grid, gridRow, gridCol, cellSize);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            RemoveFreeFormDropIndicator();
        }
        e.Handled = true;
    }
    
    private Border? _freeFormDropIndicator;
    private int _lastFreeFormRow = -1;
    private int _lastFreeFormCol = -1;
    
    /// <summary>
    /// Shows drop indicator for FreeForm grid layout
    /// </summary>
    private void ShowFreeFormDropIndicator(Grid grid, int row, int col, int cellSize)
    {
        // Only update if position changed
        if (row == _lastFreeFormRow && col == _lastFreeFormCol && _freeFormDropIndicator != null)
            return;
            
        _lastFreeFormRow = row;
        _lastFreeFormCol = col;
        
        // Remove existing indicator
        RemoveFreeFormDropIndicator();
        
        // Create the drop indicator
        _freeFormDropIndicator = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 120, 212)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(4),
            IsHitTestVisible = false,
            Tag = "FreeFormDropIndicator"
        };
        
        // Add glow effect
        _freeFormDropIndicator.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0x00, 0x78, 0xD4),
            BlurRadius = 15,
            ShadowDepth = 0,
            Opacity = 0.6
        };
        
        Grid.SetRow(_freeFormDropIndicator, row);
        Grid.SetColumn(_freeFormDropIndicator, col);
        
        grid.Children.Add(_freeFormDropIndicator);
    }
    
    /// <summary>
    /// Removes the FreeForm drop indicator
    /// </summary>
    private void RemoveFreeFormDropIndicator()
    {
        if (_freeFormDropIndicator != null && _freeFormDropIndicator.Parent is Grid parentGrid)
        {
            parentGrid.Children.Remove(_freeFormDropIndicator);
        }
        _freeFormDropIndicator = null;
        _lastFreeFormRow = -1;
        _lastFreeFormCol = -1;
    }

    private void FreeFormGrid_Drop(object sender, DragEventArgs e)
    {
        // Remove drop indicator first
        RemoveFreeFormDropIndicator();
        
        if (sender is Grid grid && e.Data.GetDataPresent("PinnedItem"))
        {
            var droppedItem = e.Data.GetData("PinnedItem") as PinnedItem;
            if (droppedItem == null) return;

            // Calculate grid position from drop point
            var dropPosition = e.GetPosition(grid);
            var showIconsOnly = _settingsService.Settings.ShowIconsOnly;
            var cellSize = showIconsOnly ? 68 : 108;

            var gridRow = (int)(dropPosition.Y / cellSize);
            var gridCol = (int)(dropPosition.X / cellSize);

            // Ensure within bounds
            gridRow = Math.Max(0, Math.Min(gridRow, grid.RowDefinitions.Count - 1));
            gridCol = Math.Max(0, Math.Min(gridCol, grid.ColumnDefinitions.Count - 1));

            // Update item position
            _pinnedItemsService.UpdateItemGridPosition(droppedItem.Id, gridRow, gridCol);
        }
        e.Handled = true;
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

    private Button CreateGroupFolderButton(Group group, string tabId, double? customSize = null)
    {
        var showIconsOnly = _settingsService.Settings.ShowIconsOnly;
        var itemCount = _pinnedItemsService.GetItemsForTab(tabId, group.Id).Count();
        
        // Calculate button size
        var buttonSize = customSize ?? (showIconsOnly ? 60.0 : 100.0);
        var isSmallMode = buttonSize < 80;
        
        // Adjust sizes based on button size
        var folderSize = Math.Max(24, buttonSize * (showIconsOnly || isSmallMode ? 0.53 : 0.48));
        var folderFontSize = Math.Max(14, buttonSize * 0.28);
        var folderMargin = showIconsOnly || isSmallMode ? new Thickness(0) : new Thickness(0, 0, 0, 8);

        // Folder visual - show mini previews of first items
        var folderVisual = new Grid
        {
            Width = folderSize,
            Height = folderSize,
            Margin = folderMargin
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
            FontSize = folderFontSize,
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

        // Create content based on mode
        UIElement buttonContent;
        var textMaxWidth = Math.Max(50, buttonSize - 20);
        
        if (showIconsOnly || isSmallMode)
        {
            // Icon only - no text label
            buttonContent = folderVisual;
        }
        else
        {
            // Full mode - icon with text label
            var folderContent = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            folderContent.Children.Add(folderVisual);
            folderContent.Children.Add(new TextBlock
            {
                Text = group.Name,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = textMaxWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            buttonContent = folderContent;
        }

        // Create button with custom size
        var button = new Button
        {
            Tag = group,
            ToolTip = $"{group.Name} ({itemCount} öğe)",
            Content = buttonContent,
            AllowDrop = true,
            Width = buttonSize,
            Height = buttonSize,
            Margin = new Thickness(4),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        
        // Apply custom style
        button.Style = CreateDynamicPinnedItemStyle();

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

        // Drag support for group reordering
        button.PreviewMouseLeftButtonDown += GroupFolder_PreviewMouseLeftButtonDown;
        button.PreviewMouseMove += GroupFolder_PreviewMouseMove;
        button.PreviewMouseLeftButtonUp += GroupFolder_PreviewMouseLeftButtonUp;

        return button;
    }

    private void GroupFolder_DragOver(object sender, DragEventArgs e)
    {
        // Handle group reorder drag over
        if (e.Data.GetDataPresent("Group"))
        {
            var droppedGroup = e.Data.GetData("Group") as Group;
            if (sender is Button btn && btn.Tag is Group targetGroup)
            {
                if (droppedGroup != null && droppedGroup.Id != targetGroup.Id)
                {
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent("PinnedItem"))
        {
            // Check if the dragged item is not already in this group
            var droppedItem = e.Data.GetData("PinnedItem") as PinnedItem;
            if (sender is Button btn && btn.Tag is Group group)
            {
                if (droppedItem != null && droppedItem.GroupId != group.Id)
                {
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.Move;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void GroupFolder_DragEnter(object sender, DragEventArgs e)
    {
        // Handle both PinnedItem and Group drag enter
        if ((e.Data.GetDataPresent("PinnedItem") || e.Data.GetDataPresent("Group")) && sender is Button button)
        {
            // For PinnedItem: Check if the dragged item is not already in this group
            if (e.Data.GetDataPresent("PinnedItem"))
            {
                var droppedItem = e.Data.GetData("PinnedItem") as PinnedItem;
                if (button.Tag is Group group && droppedItem != null && droppedItem.GroupId == group.Id)
                {
                    // Item is already in this group, don't highlight
                    return;
                }
            }
            
            // For Group: Check if it's not the same group
            if (e.Data.GetDataPresent("Group"))
            {
                var droppedGroup = e.Data.GetData("Group") as Group;
                if (button.Tag is Group targetGroup && droppedGroup != null && droppedGroup.Id == targetGroup.Id)
                {
                    // Same group, don't highlight
                    return;
                }
            }

            // Enhanced visual feedback: Scale up and add highlight border
            var scaleTransform = new ScaleTransform(1.15, 1.15);
            button.RenderTransform = scaleTransform;
            button.RenderTransformOrigin = new Point(0.5, 0.5);

            // Animate the scale for smooth effect
            var scaleAnimation = new DoubleAnimation(1.0, 1.15, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

            // Find the border inside the button template and highlight it
            if (VisualTreeHelper.GetChildrenCount(button) > 0)
            {
                var border = FindVisualChild<Border>(button);
                if (border != null)
                {
                    // Store original values for restoration
                    button.Tag = new object[] { button.Tag, border.BorderBrush, border.BorderThickness };
                    
                    // Apply highlight border
                    border.BorderBrush = (Brush)FindResource("AccentBrush");
                    border.BorderThickness = new Thickness(3);
                }
            }

            // Add a subtle glow effect
            button.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.DodgerBlue,
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = 0.8
            };
        }
    }

    private void GroupFolder_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            ResetGroupFolderVisuals(button);
        }
    }

    private void ResetGroupFolderVisuals(Button button)
    {
        // Animate scale back to normal
        if (button.RenderTransform is ScaleTransform scaleTransform)
        {
            var scaleAnimation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100));
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        }
        else
        {
            button.RenderTransform = null;
        }

        // Remove glow effect
        button.Effect = null;

        // Restore original border if we stored it
        if (button.Tag is object[] tagArray && tagArray.Length == 3)
        {
            var originalTag = tagArray[0];
            var originalBrush = tagArray[1] as Brush;
            var originalThickness = (Thickness)tagArray[2];

            if (VisualTreeHelper.GetChildrenCount(button) > 0)
            {
                var border = FindVisualChild<Border>(button);
                if (border != null)
                {
                    border.BorderBrush = originalBrush;
                    border.BorderThickness = originalThickness;
                }
            }

            // Restore original tag (the Group object)
            button.Tag = originalTag;
        }
    }

    /// <summary>
    /// Helper method to find a visual child of a specific type
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }
            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void GroupFolder_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            // Reset visual feedback
            ResetGroupFolderVisuals(button);
        }

        // Handle group reorder drop
        if (e.Data.GetDataPresent("Group") && sender is Button targetBtn)
        {
            var droppedGroup = e.Data.GetData("Group") as Group;
            Group? targetGroup = null;
            
            if (targetBtn.Tag is Group g)
            {
                targetGroup = g;
            }
            else if (targetBtn.Tag is object[] tagArray && tagArray.Length > 0 && tagArray[0] is Group arrayGroup)
            {
                targetGroup = arrayGroup;
                targetBtn.Tag = arrayGroup;
            }

            if (droppedGroup != null && targetGroup != null && droppedGroup.Id != targetGroup.Id)
            {
                // Calculate the target index based on the target group's order
                _pinnedItemsService.MoveGroup(droppedGroup.Id, targetGroup.Order);
                RefreshPinnedItems();
            }
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent("PinnedItem") && sender is Button btn)
        {
            // Get the group from the tag (handle both direct tag and array tag from DragEnter)
            Group? group = null;
            if (btn.Tag is Group g)
            {
                group = g;
            }
            else if (btn.Tag is object[] tagArray && tagArray.Length > 0 && tagArray[0] is Group arrayGroup)
            {
                group = arrayGroup;
                // Restore the original tag
                btn.Tag = arrayGroup;
            }

            if (group != null)
            {
                var droppedItem = e.Data.GetData("PinnedItem") as PinnedItem;
                if (droppedItem != null && droppedItem.GroupId != group.Id)
                {
                    // Move the item to the group
                    _pinnedItemsService.MoveItemToGroup(droppedItem.Id, group.Id);
                    
                    // Refresh UI to show the updated state
                    RefreshPinnedItems();
                }
            }
        }
        e.Handled = true;
    }

    private void GroupFolder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
        
        if (sender is Button button && button.Tag is Group group)
        {
            _draggedGroupButton = button;
            _draggedGroup = group;
        }
    }

    private void GroupFolder_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedGroup == null || _draggedGroupButton == null)
            return;

        var currentPos = e.GetPosition(null);
        var diff = _dragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragging = true;
            
            // Dim original button during drag
            _draggedGroupButton.Opacity = 0.3;
            
            var data = new DataObject("Group", _draggedGroup);
            DragDrop.DoDragDrop(_draggedGroupButton, data, DragDropEffects.Move);
            
            // Restore opacity after drag
            if (_draggedGroupButton != null)
                _draggedGroupButton.Opacity = 1.0;
            
            // Clean up drop indicators
            RemoveDropIndicator();
            RemoveFreeFormDropIndicator();
            
            _isDragging = false;
            _draggedGroup = null;
            _draggedGroupButton = null;
        }
    }

    private void GroupFolder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedGroupButton != null)
            _draggedGroupButton.Opacity = 1.0;
        
        // Clean up drop indicators
        RemoveDropIndicator();
        RemoveFreeFormDropIndicator();
            
        _isDragging = false;
        _draggedGroup = null;
        _draggedGroupButton = null;
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

    private Button CreatePinnedItemButton(PinnedItem item, double? customSize = null)
    {
        var showIconsOnly = _settingsService.Settings.ShowIconsOnly;
        
        // Calculate button size - use custom size if provided
        var buttonSize = customSize ?? (showIconsOnly ? 60.0 : 100.0);
        var isSmallMode = buttonSize < 80;
        
        // Get real icon from IconService - use special method for internet shortcuts
        var icon = item.Type == PinnedItemType.InternetShortcut 
            ? _iconService.GetInternetShortcutIcon(item.Path)
            : _iconService.GetIcon(item.Path);

        // Adjust icon size based on button size
        var iconSize = Math.Max(24, buttonSize * (showIconsOnly ? 0.53 : 0.4));
        var iconMargin = showIconsOnly || isSmallMode ? new Thickness(0) : new Thickness(0, 0, 0, 8);

        // Determine fallback emoji based on item type
        var fallbackEmoji = item.Type switch
        {
            PinnedItemType.Folder => "📁",
            PinnedItemType.InternetShortcut => "🌐",
            _ => "📦"
        };
        
        var fallbackFontSize = Math.Max(16, buttonSize * 0.32);

        var iconElement = icon != null
            ? (UIElement)new Image 
            { 
                Source = icon, 
                Width = iconSize, 
                Height = iconSize, 
                HorizontalAlignment = HorizontalAlignment.Center, 
                Margin = iconMargin 
            }
            : (UIElement)new TextBlock 
            { 
                Text = fallbackEmoji, 
                FontSize = fallbackFontSize, 
                HorizontalAlignment = HorizontalAlignment.Center, 
                Margin = iconMargin 
            };

        // Create content based on icon-only mode
        UIElement buttonContent;
        var textMaxWidth = Math.Max(50, buttonSize - 20); // Text width based on button size
        
        if (showIconsOnly || isSmallMode)
        {
            // Icon only - no text label
            buttonContent = iconElement;
        }
        else
        {
            // Full mode - icon with text label
            var nameTextBlock = new TextBlock 
            { 
                Text = item.DisplayName, 
                FontSize = 12, 
                Foreground = (Brush)FindResource("TextBrush"), 
                TextTrimming = TextTrimming.CharacterEllipsis, 
                MaxWidth = textMaxWidth, 
                HorizontalAlignment = HorizontalAlignment.Center, 
                TextAlignment = TextAlignment.Center 
            };
            
            var renameTextBox = new TextBox
            {
                Text = item.DisplayName,
                FontSize = 12,
                MaxWidth = textMaxWidth,
                MinWidth = Math.Min(60, textMaxWidth),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Visibility = Visibility.Collapsed,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2, 1, 2, 1)
            };
            renameTextBox.Tag = new object[] { item, nameTextBlock };
            renameTextBox.KeyDown += RenameTextBox_KeyDown;
            renameTextBox.LostFocus += RenameTextBox_LostFocus;
            
            var nameContainer = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            nameContainer.Children.Add(nameTextBlock);
            nameContainer.Children.Add(renameTextBox);
            
            buttonContent = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    iconElement,
                    nameContainer
                }
            };
        }

        // Create button with custom size if provided
        var button = new Button
        {
            Tag = item,
            ToolTip = item.Path,
            Content = buttonContent,
            Width = buttonSize,
            Height = buttonSize,
            Margin = new Thickness(4),
            Cursor = System.Windows.Input.Cursors.Hand,
            AllowDrop = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        
        // Apply custom template for hover effects
        button.Style = CreateDynamicPinnedItemStyle();

        button.Click += PinnedItem_Click;
        button.MouseRightButtonUp += PinnedItem_RightClick;
        
        // Drag & Drop events
        button.PreviewMouseLeftButtonDown += PinnedItem_PreviewMouseLeftButtonDown;
        button.PreviewMouseMove += PinnedItem_PreviewMouseMove;
        button.PreviewMouseLeftButtonUp += PinnedItem_PreviewMouseLeftButtonUp;

        return button;
    }
    
    /// <summary>
    /// Creates a dynamic style for pinned item buttons with hover effects
    /// </summary>
    private Style CreateDynamicPinnedItemStyle()
    {
        var style = new Style(typeof(Button));
        
        style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Button.ForegroundProperty, FindResource("TextBrush")));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
        
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "border";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(8));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(contentPresenter);
        
        template.VisualTree = border;
        
        // Add triggers for hover and pressed states
        var mouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), "border"));
        template.Triggers.Add(mouseOverTrigger);
        
        var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), "border"));
        template.Triggers.Add(pressedTrigger);
        
        style.Setters.Add(new Setter(Button.TemplateProperty, template));
        
        return style;
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
            
            // Clean up drop indicators
            RemoveDropIndicator();
            RemoveFreeFormDropIndicator();
            
            _isDragging = false;
            _draggedItem = null;
            _draggedButton = null;
        }
    }

    private void PinnedItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedButton != null)
            _draggedButton.Opacity = 1.0;
        
        // Clean up drop indicators
        RemoveDropIndicator();
        RemoveFreeFormDropIndicator();
            
        _isDragging = false;
        _draggedItem = null;
        _draggedButton = null;
    }

    private void WrapPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem") || e.Data.GetDataPresent("Group"))
        {
            e.Effects = DragDropEffects.Move;
            
            if (sender is WrapPanel wrapPanel)
            {
                var dropPosition = e.GetPosition(wrapPanel);
                var groupId = wrapPanel.Tag as string;
                var isInsideGroup = groupId != null;
                
                int dropIndex;
                if (isInsideGroup)
                {
                    dropIndex = GetDropIndex(wrapPanel, dropPosition);
                }
                else
                {
                    dropIndex = GetGlobalDropIndex(wrapPanel, dropPosition);
                }
                
                // Only update indicator if index changed
                if (dropIndex != _lastDropIndex)
                {
                    _lastDropIndex = dropIndex;
                    ShowDropIndicator(wrapPanel, dropIndex);
                }
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            RemoveDropIndicator();
        }
        e.Handled = true;
    }
    
    /// <summary>
    /// Creates and shows a visual drop indicator at the specified index
    /// </summary>
    private void ShowDropIndicator(WrapPanel wrapPanel, int dropIndex)
    {
        // Remove existing indicator
        RemoveDropIndicator();
        
        var showIconsOnly = _settingsService.Settings.ShowIconsOnly;
        var indicatorHeight = showIconsOnly ? 60.0 : 100.0;
        
        // Create the drop indicator
        _dropIndicator = new Border
        {
            Width = 4,
            Height = indicatorHeight,
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), // Accent blue
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(2, 4, 2, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "DropIndicator"
        };
        
        // Add glow effect
        _dropIndicator.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0x00, 0x78, 0xD4),
            BlurRadius = 10,
            ShadowDepth = 0,
            Opacity = 0.8
        };
        
        // Insert at the correct position
        var insertIndex = 0;
        var currentElementIndex = 0;
        
        for (int i = 0; i < wrapPanel.Children.Count; i++)
        {
            var child = wrapPanel.Children[i];
            
            // Skip existing drop indicators
            if (child is Border border && border.Tag as string == "DropIndicator")
                continue;
                
            if (child is Button btn && (btn.Tag is PinnedItem || btn.Tag is Group))
            {
                if (currentElementIndex == dropIndex)
                {
                    insertIndex = i;
                    break;
                }
                currentElementIndex++;
            }
            insertIndex = i + 1;
        }
        
        // Clamp insert index
        insertIndex = Math.Min(insertIndex, wrapPanel.Children.Count);
        
        wrapPanel.Children.Insert(insertIndex, _dropIndicator);
    }
    
    /// <summary>
    /// Removes the drop indicator from the panel
    /// </summary>
    private void RemoveDropIndicator()
    {
        if (_dropIndicator != null && _dropIndicator.Parent is Panel parent)
        {
            parent.Children.Remove(_dropIndicator);
        }
        _dropIndicator = null;
        _lastDropIndex = -1;
    }

    private void WrapPanel_Drop(object sender, DragEventArgs e)
    {
        // Remove drop indicator first
        RemoveDropIndicator();
        
        if (sender is not WrapPanel wrapPanel) return;
        
        var groupId = wrapPanel.Tag as string;
        var dropPosition = e.GetPosition(wrapPanel);
        
        // Check if we're inside a group folder (has a Tag with groupId)
        var isInsideGroup = groupId != null;
        
        // Handle group reorder drop on main WrapPanel (not inside a group)
        if (e.Data.GetDataPresent("Group") && !isInsideGroup)
        {
            var droppedGroup = e.Data.GetData("Group") as Group;
            if (droppedGroup == null) return;

            // Use global index for mixed sorting
            var globalDropIndex = GetGlobalDropIndex(wrapPanel, dropPosition);
            _pinnedItemsService.MoveElementToGlobalPosition(droppedGroup.Id, true, globalDropIndex, _currentTabId);
            RefreshPinnedItems();
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent("PinnedItem"))
        {
            var droppedItem = e.Data.GetData("PinnedItem") as PinnedItem;
            if (droppedItem == null) return;

            // If dropping inside a group
            if (isInsideGroup)
            {
                // Move to group if not already in it
                if (droppedItem.GroupId != groupId)
                {
                    _pinnedItemsService.MoveItemToGroup(droppedItem.Id, groupId);
                }
                
                // Get index within group
                var dropIndex = GetDropIndex(wrapPanel, dropPosition);
                _pinnedItemsService.MoveItem(droppedItem.Id, dropIndex, _currentTabId, groupId);
            }
            else
            {
                // Dropping on main panel - use global ordering
                // First, ensure item is ungrouped
                if (droppedItem.GroupId != null)
                {
                    _pinnedItemsService.MoveItemToGroup(droppedItem.Id, null);
                }
                
                // Use global index for mixed sorting
                var globalDropIndex = GetGlobalDropIndex(wrapPanel, dropPosition);
                _pinnedItemsService.MoveElementToGlobalPosition(droppedItem.Id, false, globalDropIndex, _currentTabId);
            }
            
            RefreshPinnedItems();
        }
        e.Handled = true;
    }
    
    /// <summary>
    /// Get global drop index considering both PinnedItems and Groups mixed together
    /// </summary>
    private int GetGlobalDropIndex(WrapPanel wrapPanel, Point dropPosition)
    {
        int globalIndex = 0;
        
        for (int i = 0; i < wrapPanel.Children.Count; i++)
        {
            var child = wrapPanel.Children[i];
            
            // Check if this child is a valid draggable element (PinnedItem or Group)
            if (child is Button btn && (btn.Tag is PinnedItem || btn.Tag is Group))
            {
                var childPos = child.TranslatePoint(new Point(0, 0), wrapPanel);
                var childRect = new Rect(childPos, new Size(((FrameworkElement)child).ActualWidth, ((FrameworkElement)child).ActualHeight));
                
                // Check if drop position is before the center of this child
                if (dropPosition.X < childRect.Left + childRect.Width / 2)
                {
                    return globalIndex;
                }
                
                globalIndex++;
            }
        }
        return globalIndex;
    }

    private int GetGroupDropIndex(WrapPanel wrapPanel, Point dropPosition)
    {
        int groupIndex = 0;
        for (int i = 0; i < wrapPanel.Children.Count; i++)
        {
            var child = wrapPanel.Children[i];
            
            // Only consider group folder buttons
            if (child is Button btn && btn.Tag is Group)
            {
                var childPos = child.TranslatePoint(new Point(0, 0), wrapPanel);
                var childRect = new Rect(childPos, new Size(((FrameworkElement)child).ActualWidth, ((FrameworkElement)child).ActualHeight));
                
                if (dropPosition.X < childRect.Left + childRect.Width / 2)
                {
                    return groupIndex;
                }
                groupIndex++;
            }
        }
        return groupIndex;
    }

    private int GetDropIndex(WrapPanel wrapPanel, Point dropPosition)
    {
        int itemIndex = 0; // Index only for PinnedItem elements
        
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
                return itemIndex;
            }
            
            itemIndex++;
        }
        return itemIndex;
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

            // Rename menu item
            var renameItem = new MenuItem { Header = "Yeniden Adlandır" };
            renameItem.Click += (s, args) => StartInlineRename(button, item);

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
            contextMenu.Items.Add(renameItem);
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

    #endregion

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToSettingsView();
    }

    private void SwitchToSettingsView()
    {
        // Hide other views
        PinnedItemsView.Visibility = Visibility.Collapsed;
        SearchView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        _isInSearchMode = false;
        
        // Hide tabs and show close button header with Ayarlar title
        TabBarPanel.Visibility = Visibility.Collapsed;
        CloseButtonHeader.Visibility = Visibility.Visible;
        ViewTitleText.Text = "Ayarlar";
        
        // Load current settings into controls
        LoadSettingsIntoControls();
    }

    private void CloseViewButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToPinnedView();
    }

    private void LoadSettingsIntoControls()
    {
        var settings = _settingsService.Settings;
        
        // Icons Only
        IconsOnlyCheckBox.IsChecked = settings.ShowIconsOnly;
        
        // Transparency
        TransparencySlider.Value = settings.MenuTransparency * 100;
        TransparencyLabel.Text = $"Menü saydamlığı: {(int)(settings.MenuTransparency * 100)}%";
        
        // Layout Mode
        LayoutModeComboBox.SelectedIndex = settings.PinnedItemsLayout == LayoutMode.Ordered ? 0 : 1;
        
        // Web Search URL
        WebSearchUrlTextBox.Text = settings.WebSearchUrl;

        // Animations
        AnimationsCheckBox.IsChecked = settings.EnableAnimations;

        // Position
        PositionComboBox.SelectedIndex = settings.Position == MenuPosition.Left ? 0 : 1;

        // Size
        SizeComboBox.SelectedIndex = settings.Size switch
        {
            MenuSize.Small => 0,
            MenuSize.Normal => 1,
            MenuSize.Large => 2,
            MenuSize.VeryLarge => 3,
            MenuSize.Fullscreen => 4,
            MenuSize.Custom => 5,
            _ => 1
        };
        CustomSizePanel.Visibility = settings.Size == MenuSize.Custom ? Visibility.Visible : Visibility.Collapsed;
        CustomWidthTextBox.Text = settings.CustomWidth.ToString();
        CustomHeightTextBox.Text = settings.CustomHeight.ToString();

        // Item Size
        ItemSizeSlider.Value = settings.ItemSize;
        ItemSizeLabel.Text = $"Öğe boyutu: {settings.ItemSize}px";

        // Override Windows Key
        OverrideWinKeyCheckBox.IsChecked = settings.OverrideWindowsStartButton;

        // Background Darkness
        BackgroundDarknessSlider.Value = settings.BackgroundDarkness;
        BackgroundDarknessLabel.Text = $"Arka plan koyuluğu: {settings.BackgroundDarkness}";

        // Hotkey - update display only
        UpdateHotkeyDisplayText();
    }

    private void SettingsBackButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToPinnedView();
    }

    private void IconsOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (IconsOnlyCheckBox.IsChecked.HasValue)
        {
            _settingsService.UpdateSetting(nameof(AppSettings.ShowIconsOnly), IconsOnlyCheckBox.IsChecked.Value);
        }
    }

    private void TransparencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TransparencyLabel != null && _settingsService != null)
        {
            var value = e.NewValue / 100.0;
            TransparencyLabel.Text = $"Menü saydamlığı: {(int)e.NewValue}%";
            _settingsService.UpdateSetting(nameof(AppSettings.MenuTransparency), value);
        }
    }

    private void LayoutModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayoutModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string modeStr)
        {
            var mode = modeStr == "Ordered" ? LayoutMode.Ordered : LayoutMode.FreeForm;
            var previousMode = _settingsService.Settings.PinnedItemsLayout;
            
            // When switching from FreeForm to Ordered, clear grid positions
            if (previousMode == LayoutMode.FreeForm && mode == LayoutMode.Ordered)
            {
                foreach (var tab in _pinnedItemsService.Tabs)
                {
                    _pinnedItemsService.ClearGridPositionsForTab(tab.Id);
                }
            }
            
            _settingsService.UpdateSetting(nameof(AppSettings.PinnedItemsLayout), mode);
        }
    }

    private void WebSearchUrlTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var url = WebSearchUrlTextBox.Text?.Trim();
        if (!string.IsNullOrEmpty(url))
        {
            _settingsService.UpdateSetting(nameof(AppSettings.WebSearchUrl), url);
        }
    }

    #region New Settings Event Handlers

    private void AnimationsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (AnimationsCheckBox.IsChecked.HasValue)
        {
            _settingsService.UpdateSetting(nameof(AppSettings.EnableAnimations), AnimationsCheckBox.IsChecked.Value);
        }
    }

    private void PositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PositionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string posStr)
        {
            var position = posStr == "Left" ? MenuPosition.Left : MenuPosition.Center;
            _settingsService.UpdateSetting(nameof(AppSettings.Position), position);
            PositionWindow(); // Apply immediately
        }
    }

    private void SizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SizeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string sizeStr)
        {
            var size = sizeStr switch
            {
                "Small" => MenuSize.Small,
                "Normal" => MenuSize.Normal,
                "Large" => MenuSize.Large,
                "VeryLarge" => MenuSize.VeryLarge,
                "Fullscreen" => MenuSize.Fullscreen,
                "Custom" => MenuSize.Custom,
                _ => MenuSize.Normal
            };
            
            CustomSizePanel.Visibility = size == MenuSize.Custom ? Visibility.Visible : Visibility.Collapsed;
            _settingsService.UpdateSetting(nameof(AppSettings.Size), size);
            PositionWindow(); // Apply immediately
        }
    }

    private void CustomSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(CustomWidthTextBox.Text, out int width))
        {
            _settingsService.UpdateSetting(nameof(AppSettings.CustomWidth), Math.Clamp(width, 400, 2000));
        }
        if (int.TryParse(CustomHeightTextBox.Text, out int height))
        {
            _settingsService.UpdateSetting(nameof(AppSettings.CustomHeight), Math.Clamp(height, 400, 2000));
        }
        PositionWindow(); // Apply immediately
    }

    private void ItemSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ItemSizeLabel != null && _settingsService != null)
        {
            var value = (int)e.NewValue;
            ItemSizeLabel.Text = $"Öğe boyutu: {value}px";
            _settingsService.UpdateSetting(nameof(AppSettings.ItemSize), value);
            RefreshPinnedItems(); // Apply immediately
        }
    }

    private void OverrideWinKeyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (OverrideWinKeyCheckBox.IsChecked.HasValue)
        {
            _settingsService.UpdateSetting(nameof(AppSettings.OverrideWindowsStartButton), OverrideWinKeyCheckBox.IsChecked.Value);
        }
    }

    // Hotkey assignment state
    private bool _isAssigningHotkey = false;
    private HotkeyConfig? _pendingHotkey = null;

    private void HotkeyAssignButton_Click(object sender, RoutedEventArgs e)
    {
        // Start assignment
        StartHotkeyAssignment();
    }

    private void HotkeySaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingHotkey != null && 
            (_pendingHotkey.UseWinKey || _pendingHotkey.Ctrl || _pendingHotkey.Alt || _pendingHotkey.Shift || _pendingHotkey.KeyCode > 0))
        {
            // Save the hotkey
            _settingsService.UpdateSetting(nameof(AppSettings.OpenMenuHotkey), _pendingHotkey);
        }
        CancelHotkeyAssignment();
    }

    private void HotkeyCancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelHotkeyAssignment();
    }

    private void StartHotkeyAssignment()
    {
        _isAssigningHotkey = true;
        _pendingHotkey = new HotkeyConfig { UseWinKey = false };
        
        // Suppress Win key and listen for key presses from hook
        App.Instance.KeyboardHookService.SuppressWinKey = true;
        App.Instance.KeyboardHookService.KeyPressedForAssignment += OnKeyPressedForAssignment;
        
        HotkeyAssignButton.Visibility = Visibility.Collapsed;
        HotkeySaveButton.Visibility = Visibility.Visible;
        HotkeyCancelButton.Visibility = Visibility.Visible;
        
        HotkeyDisplayBorder.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0x00));
        HotkeyDisplayText.Text = "Tuş kombinasyonuna basın...";
        HotkeyDisplayText.Foreground = new SolidColorBrush(Colors.Yellow);
        HotkeyInstructionText.Text = "Kısayol tuşlarına aynı anda basın (Win, Ctrl, Alt, Shift ve bir tuş). Kaydet ile uygulayın.";
        HotkeyInstructionText.Visibility = Visibility.Visible;
        
        // Focus to capture key events
        this.Focus();
    }

    private void CancelHotkeyAssignment()
    {
        _isAssigningHotkey = false;
        _pendingHotkey = null;
        
        // Re-enable Win key and unsubscribe from event
        App.Instance.KeyboardHookService.KeyPressedForAssignment -= OnKeyPressedForAssignment;
        App.Instance.KeyboardHookService.SuppressWinKey = false;
        
        HotkeyAssignButton.Visibility = Visibility.Visible;
        HotkeySaveButton.Visibility = Visibility.Collapsed;
        HotkeyCancelButton.Visibility = Visibility.Collapsed;
        
        HotkeyDisplayBorder.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
        HotkeyInstructionText.Visibility = Visibility.Collapsed;
        UpdateHotkeyDisplayText();
    }

    private void OnKeyPressedForAssignment(object? sender, KeyPressedEventArgs e)
    {
        // This is called from the keyboard hook, need to dispatch to UI thread
        Dispatcher.Invoke(() =>
        {
            if (!_isAssigningHotkey || _pendingHotkey == null) return;

            // Update pending hotkey
            _pendingHotkey.UseWinKey = e.IsWinKey;
            _pendingHotkey.Ctrl = e.IsCtrlPressed;
            _pendingHotkey.Alt = e.IsAltPressed;
            _pendingHotkey.Shift = e.IsShiftPressed;

            // Check if it's a non-modifier key
            int vk = e.VirtualKeyCode;
            const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
            const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
            const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;
            const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
            const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10;

            bool isModifierKey = vk == VK_LWIN || vk == VK_RWIN ||
                                 vk == VK_LCONTROL || vk == VK_RCONTROL || vk == VK_CONTROL ||
                                 vk == VK_LMENU || vk == VK_RMENU || vk == VK_MENU ||
                                 vk == VK_LSHIFT || vk == VK_RSHIFT || vk == VK_SHIFT;

            if (!isModifierKey)
            {
                _pendingHotkey.KeyCode = vk;
            }

            UpdatePendingHotkeyDisplay();
        });
    }

    private void HandleHotkeyAssignmentKeyDown(KeyEventArgs e)
    {
        if (!_isAssigningHotkey || _pendingHotkey == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        
        // Handle Escape to cancel
        if (key == Key.Escape)
        {
            CancelHotkeyAssignment();
            e.Handled = true;
            return;
        }

        // The rest is now handled by OnKeyPressedForAssignment via the keyboard hook
        e.Handled = true;
    }

    private void UpdatePendingHotkeyDisplay()
    {
        if (_pendingHotkey == null) return;
        
        var display = _pendingHotkey.ToString();
        if (string.IsNullOrEmpty(display) || display == "Win" && !_pendingHotkey.UseWinKey)
        {
            display = "Tuş kombinasyonuna basın...";
        }
        HotkeyDisplayText.Text = display;
    }

    private void UpdateHotkeyDisplayText()
    {
        if (HotkeyDisplayText == null || _settingsService == null) return;
        
        var hotkey = _settingsService.Settings.OpenMenuHotkey ?? new HotkeyConfig();
        HotkeyDisplayText.Text = hotkey.ToString();
        HotkeyDisplayText.Foreground = (Brush)FindResource("TextBrush");
    }

    private void ThemeColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string colorHex)
        {
            _settingsService.UpdateSetting(nameof(AppSettings.AccentColor), colorHex);
            App.Instance.ApplyThemeColor(colorHex);
        }
    }

    private void BackgroundDarknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundDarknessLabel != null && _settingsService != null)
        {
            var value = (int)e.NewValue;
            BackgroundDarknessLabel.Text = $"Arka plan koyuluğu: {value}";
            _settingsService.UpdateSetting(nameof(AppSettings.BackgroundDarkness), value);
            ApplyTransparency(); // Apply immediately
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
