using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomStartMenu.Models;

namespace CustomStartMenu.Views;

/// <summary>
/// Grid-based pinned items display for the Start Menu with pagination
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
                ShowGroupContent(group, currentTabId);
                return;
            }
            else
            {
                _openGroupId = null;
            }
        }

        var groups = _pinnedItemsService.GetGroupsForTab(currentTabId).ToList();
        var ungroupedItems = _pinnedItemsService.GetUngroupedItemsForTab(currentTabId).ToList();
        var totalItems = ungroupedItems.Count + groups.Count;

        if (totalItems == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            HidePaginationControls();
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        
        RenderGridLayout(ungroupedItems, groups, currentTabId);
    }

    /// <summary>
    /// Render items and groups in a grid layout with pagination
    /// </summary>
    private void RenderGridLayout(List<PinnedItem> items, List<Group> groups, string currentTabId)
    {
        var itemSize = _settingsService.Settings.ItemSize;
        var cellSize = itemSize + 8;
        var layoutMode = _settingsService.Settings.PinnedItemsLayout;
        
        var availableWidth = PinnedScrollViewer.ActualWidth > 0 ? PinnedScrollViewer.ActualWidth - 16 : 600;
        // Reserve space for pagination buttons (50px)
        var availableHeight = PinnedScrollViewer.ActualHeight > 50 ? PinnedScrollViewer.ActualHeight - 50 : 400;
        var columns = Math.Max(1, (int)(availableWidth / cellSize));
        var rowsPerPage = Math.Max(1, (int)(availableHeight / cellSize));
        var itemsPerPage = columns * rowsPerPage;
        
        // In Ordered mode, compact items first (without firing event to prevent infinite loop)
        if (layoutMode == LayoutMode.Ordered)
        {
            _pinnedItemsService.CompactItems(currentTabId, null, columns, fireEvent: false);
            // Refresh lists after compacting
            items = _pinnedItemsService.GetUngroupedItemsForTab(currentTabId).ToList();
            groups = _pinnedItemsService.GetGroupsForTab(currentTabId).ToList();
        }

        // Combine all elements for pagination
        var allElements = new List<object>();
        
        if (layoutMode == LayoutMode.Ordered)
        {
            // In ordered mode, combine and sort by position
            var combined = items.Cast<object>().Concat(groups.Cast<object>())
                .OrderBy(e => e is PinnedItem pi ? pi.GridRow : ((Group)e).GridRow)
                .ThenBy(e => e is PinnedItem pi ? pi.GridColumn : ((Group)e).GridColumn)
                .ToList();
            allElements = combined;
        }
        else
        {
            // In FreeForm mode, use grid positions
            allElements = items.Cast<object>().Concat(groups.Cast<object>()).ToList();
        }

        // Calculate pagination
        var totalElements = allElements.Count;
        _totalPages = Math.Max(1, (int)Math.Ceiling((double)totalElements / itemsPerPage));
        
        // Clamp current page
        if (_currentPage >= _totalPages) _currentPage = _totalPages - 1;
        if (_currentPage < 0) _currentPage = 0;
        
        // Update pagination controls
        UpdatePaginationControls();
        
        // Get elements for current page
        List<object> pageElements;
        if (layoutMode == LayoutMode.Ordered)
        {
            pageElements = allElements.Skip(_currentPage * itemsPerPage).Take(itemsPerPage).ToList();
        }
        else
        {
            // FreeForm mode: filter by row range
            int startRow = _currentPage * rowsPerPage;
            int endRow = startRow + rowsPerPage;
            pageElements = allElements.Where(e =>
            {
                int row = e is PinnedItem pi ? pi.GridRow : ((Group)e).GridRow;
                return row >= startRow && row < endRow;
            }).ToList();
        }
        
        // Create grid
        var grid = new Grid
        {
            AllowDrop = true,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        
        // Attach drag-drop events
        grid.DragOver += ItemsGrid_DragOver;
        grid.DragLeave += ItemsGrid_DragLeave;
        grid.Drop += ItemsGrid_Drop;
        grid.MouseRightButtonUp += ItemsGrid_MouseRightButtonUp;

        // Define columns
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cellSize) });
        }

        // Define rows for this page
        for (int r = 0; r < rowsPerPage; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellSize) });
        }

        // Place elements
        int index = 0;
        int startRowOffset = _currentPage * rowsPerPage;
        
        foreach (var element in pageElements)
        {
            int row, col;
            
            if (layoutMode == LayoutMode.Ordered)
            {
                // Sequential placement
                row = index / columns;
                col = index % columns;
                index++;
            }
            else
            {
                // Use stored positions, adjusted for page offset
                if (element is PinnedItem pi)
                {
                    row = pi.GridRow - startRowOffset;
                    col = pi.GridColumn;
                }
                else
                {
                    var g = (Group)element;
                    row = g.GridRow - startRowOffset;
                    col = g.GridColumn;
                }
            }
            
            if (row >= 0 && row < rowsPerPage && col >= 0 && col < columns)
            {
                Button button;
                if (element is PinnedItem item)
                {
                    button = CreatePinnedItemButton(item, itemSize);
                }
                else
                {
                    var group = (Group)element;
                    button = CreateGroupFolderButton(group, currentTabId, itemSize);
                }
                
                Grid.SetRow(button, row);
                Grid.SetColumn(button, col);
                grid.Children.Add(button);
            }
        }

        PinnedItemsContainer.Children.Add(grid);
    }

    private void UpdatePaginationControls()
    {
        if (_totalPages > 1)
        {
            PaginationPanel.Visibility = Visibility.Visible;
            PrevPageButton.Visibility = _currentPage > 0 ? Visibility.Visible : Visibility.Hidden;
            NextPageButton.Visibility = _currentPage < _totalPages - 1 ? Visibility.Visible : Visibility.Hidden;
        }
        else
        {
            HidePaginationControls();
        }
    }

    private void HidePaginationControls()
    {
        PaginationPanel.Visibility = Visibility.Collapsed;
        PrevPageButton.Visibility = Visibility.Collapsed;
        NextPageButton.Visibility = Visibility.Collapsed;
    }

    private void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            RefreshPinnedItems();
        }
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages - 1)
        {
            _currentPage++;
            RefreshPinnedItems();
        }
    }

    private void ResetPageOnTabChange()
    {
        _currentPage = 0;
    }

    private void ShowGroupContent(Group group, string tabId)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        HidePaginationControls(); // Hide pagination in group view

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

        // Group items in grid
        var items = _pinnedItemsService.GetItemsForTab(tabId, group.Id).ToList();
        
        if (items.Any())
        {
            var itemSize = _settingsService.Settings.ItemSize;
            var cellSize = itemSize + 8;
            var availableWidth = PinnedScrollViewer.ActualWidth > 0 ? PinnedScrollViewer.ActualWidth - 16 : 600;
            var columns = Math.Max(1, (int)(availableWidth / cellSize));
            
            // In Ordered mode, compact items (without firing event to prevent infinite loop)
            if (_settingsService.Settings.PinnedItemsLayout == LayoutMode.Ordered)
            {
                _pinnedItemsService.CompactItems(tabId, group.Id, columns, fireEvent: false);
                items = _pinnedItemsService.GetItemsForTab(tabId, group.Id).ToList();
            }
            
            int maxRow = items.Any() ? items.Max(i => i.GridRow) : 0;
            var rows = maxRow + 2;
            
            var grid = new Grid
            {
                AllowDrop = true,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            grid.DragOver += ItemsGrid_DragOver;
            grid.DragLeave += ItemsGrid_DragLeave;
            grid.Drop += ItemsGrid_Drop;

            for (int c = 0; c < columns; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cellSize) });
            }

            for (int r = 0; r < rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellSize) });
            }

            foreach (var item in items)
            {
                if (item.GridRow < rows && item.GridColumn < columns)
                {
                    var button = CreatePinnedItemButton(item, itemSize);
                    Grid.SetRow(button, item.GridRow);
                    Grid.SetColumn(button, item.GridColumn);
                    grid.Children.Add(button);
                }
            }

            contentPanel.Children.Add(grid);
        }
        else
        {
            var emptyText = new TextBlock
            {
                Text = "Bu klasör boş. Öğeleri buraya sürükleyebilirsiniz.",
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

    private Button CreateGroupFolderButton(Group group, string tabId, double buttonSize)
    {
        var showIconsOnly = _settingsService.Settings.ShowIconsOnly;
        var itemCount = _pinnedItemsService.GetItemsForTab(tabId, group.Id).Count();
        var isSmallMode = buttonSize < 80;
        
        var folderSize = Math.Max(24, buttonSize * (showIconsOnly || isSmallMode ? 0.53 : 0.48));
        var folderFontSize = Math.Max(14, buttonSize * 0.28);
        var folderMargin = showIconsOnly || isSmallMode ? new Thickness(0) : new Thickness(0, 0, 0, 8);

        var folderVisual = new Grid
        {
            Width = folderSize,
            Height = folderSize,
            Margin = folderMargin
        };

        var folderBg = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 200, 100)),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 200, 100)),
            BorderThickness = new Thickness(2)
        };
        folderVisual.Children.Add(folderBg);

        var folderIcon = new TextBlock
        {
            Text = "📁",
            FontSize = folderFontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        folderVisual.Children.Add(folderIcon);

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

        UIElement buttonContent;
        var textMaxWidth = Math.Max(50, buttonSize - 20);
        
        if (showIconsOnly || isSmallMode)
        {
            buttonContent = folderVisual;
        }
        else
        {
            var folderContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
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

        var button = new Button
        {
            Tag = group,
            ToolTip = $"{group.Name} ({itemCount} öğe)",
            Content = buttonContent,
            AllowDrop = true,
            Width = buttonSize,
            Height = buttonSize,
            Margin = new Thickness(4),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        
        button.Style = CreateDynamicPinnedItemStyle();

        // Click to open folder
        button.Click += (s, e) =>
        {
            if (!_isDragging)
            {
                _openGroupId = group.Id;
                RefreshPinnedItems();
            }
        };

        // Right-click context menu
        button.MouseRightButtonUp += (s, e) =>
        {
            ShowGroupContextMenu(group, button);
            e.Handled = true;
        };

        // Drop item onto folder
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

    private void ShowGroupContextMenu(Group group, UIElement target)
    {
        var contextMenu = new ContextMenu();

        var renameItem = new MenuItem { Header = "Klasörü Yeniden Adlandır" };
        renameItem.Click += (s, e) => ShowRenameGroupDialog(group);

        var deleteItem = new MenuItem { Header = "Klasörü Sil" };
        deleteItem.Click += (s, e) =>
        {
            var result = MessageBox.Show(
                $"'{group.Name}' klasörünü silmek istediğinizden emin misiniz?\nİçindeki öğeler klasörsüz olacak.",
                "Klasörü Sil",
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
            Title = "Klasörü Yeniden Adlandır",
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

    private Button CreatePinnedItemButton(PinnedItem item, double buttonSize)
    {
        var showIconsOnly = _settingsService.Settings.ShowIconsOnly;
        var isSmallMode = buttonSize < 80;
        
        var icon = item.Type == PinnedItemType.InternetShortcut 
            ? _iconService.GetInternetShortcutIcon(item.Path)
            : _iconService.GetIcon(item.Path);

        var iconSize = Math.Max(24, buttonSize * (showIconsOnly ? 0.53 : 0.4));
        var iconMargin = showIconsOnly || isSmallMode ? new Thickness(0) : new Thickness(0, 0, 0, 8);

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

        UIElement buttonContent;
        var textMaxWidth = Math.Max(50, buttonSize - 20);
        
        if (showIconsOnly || isSmallMode)
        {
            buttonContent = iconElement;
        }
        else
        {
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
            
            var nameContainer = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            nameContainer.Children.Add(nameTextBlock);
            nameContainer.Children.Add(renameTextBox);
            
            buttonContent = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { iconElement, nameContainer }
            };
        }

        var button = new Button
        {
            Tag = item,
            ToolTip = item.Path,
            Content = buttonContent,
            Width = buttonSize,
            Height = buttonSize,
            Margin = new Thickness(4),
            Cursor = Cursors.Hand,
            AllowDrop = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        
        button.Style = CreateDynamicPinnedItemStyle();

        button.Click += PinnedItem_Click;
        button.MouseRightButtonUp += PinnedItem_RightClick;
        
        // Drag events
        button.PreviewMouseLeftButtonDown += PinnedItem_PreviewMouseLeftButtonDown;
        button.PreviewMouseMove += PinnedItem_PreviewMouseMove;
        button.PreviewMouseLeftButtonUp += PinnedItem_PreviewMouseLeftButtonUp;

        return button;
    }
    
    private Style CreateDynamicPinnedItemStyle()
    {
        var style = new Style(typeof(Button));
        
        style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Button.ForegroundProperty, FindResource("TextBrush")));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
        
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
            var gridColumns = CalculateGridColumns();

            var openItem = new MenuItem { Header = "Aç" };
            openItem.Click += (s, args) => LaunchItem(item.Path);

            var openLocationItem = new MenuItem { Header = "Dosya konumunu aç" };
            openLocationItem.Click += (s, args) => OpenFileLocation(item.Path);

            var renameItem = new MenuItem { Header = "Yeniden Adlandır" };
            renameItem.Click += (s, args) => StartInlineRename(button, item);

            // Move to group submenu
            var moveToGroupMenu = new MenuItem { Header = "Klasöre Ekle" };
            
            var noGroupItem = new MenuItem { Header = "(Klasörsüz)" };
            noGroupItem.Click += (s, args) => _pinnedItemsService.MoveItemToGroup(item.Id, null, gridColumns);
            moveToGroupMenu.Items.Add(noGroupItem);
            
            var groups = _pinnedItemsService.GetGroupsForTab(_currentTabId).ToList();
            if (groups.Any())
            {
                moveToGroupMenu.Items.Add(new Separator());
                foreach (var group in groups)
                {
                    var groupItem = new MenuItem { Header = group.Name };
                    groupItem.Click += (s, args) => _pinnedItemsService.MoveItemToGroup(item.Id, group.Id, gridColumns);
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
                tabItem.Click += (s, args) => _pinnedItemsService.MoveItemToTab(item.Id, tab.Id, gridColumns);
                moveToTabMenu.Items.Add(tabItem);
            }

            var unpinItem = new MenuItem { Header = "Sabitlemeyi kaldır" };
            unpinItem.Click += (s, args) => _pinnedItemsService.RemovePinById(item.Id);

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

    private void ShowCreateGroupDialog()
    {
        var dialog = new Window
        {
            Title = "Yeni Klasör",
            Width = 300,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        var textBox = new TextBox { Text = "Yeni Klasör", Margin = new Thickness(0, 0, 0, 12) };
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        
        var okButton = new Button { Content = "Oluştur", Width = 75, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "İptal", Width = 75 };

        okButton.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                var gridColumns = CalculateGridColumns();
                _pinnedItemsService.AddGroup(textBox.Text, _currentTabId, gridColumns);
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

    // NOTE: Inline rename methods (StartInlineRename, RenameTextBox_KeyDown, etc.) 
    // are defined in StartMenuWindow.Launch.cs
}
