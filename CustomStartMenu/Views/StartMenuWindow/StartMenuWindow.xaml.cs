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

/// <summary>
/// Main window class for the Custom Start Menu.
/// This is a partial class - functionality is split across multiple files:
/// - StartMenuWindow.xaml.cs (this file) - Core window logic, lifecycle, event handlers
/// - StartMenuWindow.Interop.cs - Win32 API definitions
/// - StartMenuWindow.Search.cs - Search functionality
/// - StartMenuWindow.Tabs.cs - Tab management
/// - StartMenuWindow.PinnedItems.cs - Pinned items display
/// - StartMenuWindow.DragDrop.cs - Drag and drop handling
/// - StartMenuWindow.Settings.cs - Settings panel
/// - StartMenuWindow.Power.cs - Power actions (shutdown, restart, etc.)
/// - StartMenuWindow.Launch.cs - Launch and file operations
/// </summary>
public partial class StartMenuWindow : Window
{
    #region Fields

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
    
    // Drag & Drop (grid-based)
    private Point _dragStartPoint;
    private bool _isDragging;
    private PinnedItem? _draggedItem;
    private Group? _draggedGroup;
    private Button? _draggedButton;
    private Border? _dropIndicator;
    
    // Prevent duplicate character input
    private DateTime _lastCharInputTime = DateTime.MinValue;
    private char _lastCharInput = '\0';
    
    // Group folder original border state for drag visual feedback
    private readonly Dictionary<Button, (Brush? BorderBrush, Thickness BorderThickness)> _groupFolderOriginalBorders = new();
    
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

    // Pagination
    private int _currentPage = 0;
    private int _totalPages = 1;

    #endregion

    #region Constructor

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

    #endregion

    #region Window Lifecycle

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

    #endregion

    #region Show/Hide Menu

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

    #endregion

    #region Window Event Handlers

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
        
        // Don't interfere if already in search mode and SearchBox has focus
        // This prevents double character input
        if (_isInSearchMode && SearchBox.IsFocused)
        {
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
        
        // Don't switch to search mode if inline tab edit is active (new tab name / rename tab)
        if (_activeTabEditTextBox != null)
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
            // Track this input to prevent duplicate from global hook
            if (e.Text.Length > 0)
            {
                _lastCharInputTime = DateTime.Now;
                _lastCharInput = e.Text[0];
            }
            
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
            
            // Prevent duplicate input - if same character was just processed within 50ms, skip
            var now = DateTime.Now;
            if (character == _lastCharInput && (now - _lastCharInputTime).TotalMilliseconds < 50)
            {
                return;
            }
            _lastCharInputTime = now;
            _lastCharInput = character;
            
            // Don't add character if SearchBox already has focus (WPF will handle it)
            if (_isInSearchMode && SearchBox.IsFocused) return;
            
            // Don't switch to search mode if assigning hotkey
            if (_isAssigningHotkey) return;
            
            // Don't switch to search mode if inline rename is active
            if (_activeRenameTextBox != null) return;
            
            // Don't switch to search mode if inline tab edit is active (new tab name / rename tab)
            if (_activeTabEditTextBox != null) return;
            
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
                // Already in search mode but SearchBox doesn't have focus, append character
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
                // With pagination, no scrolling needed
                return false;
            }
        }
        
        return false;
    }

    #endregion

    #region Window Closing

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

    #endregion
}
