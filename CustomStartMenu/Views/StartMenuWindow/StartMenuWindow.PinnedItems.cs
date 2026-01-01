using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CustomStartMenu.Models;

namespace CustomStartMenu.Views;

/// <summary>
/// Pinned items display and management for the Start Menu
/// </summary>
public partial class StartMenuWindow
{
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
}
