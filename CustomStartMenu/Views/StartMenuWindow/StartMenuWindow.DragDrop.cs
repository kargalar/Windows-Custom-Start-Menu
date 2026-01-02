using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CustomStartMenu.Models;

namespace CustomStartMenu.Views;

/// <summary>
/// Grid-based Drag & Drop functionality for the Start Menu
/// </summary>
public partial class StartMenuWindow
{
    // NOTE: Drag state fields are defined in StartMenuWindow.xaml.cs:
    // _dragStartPoint, _isDragging, _draggedItem, _draggedGroup, _draggedButton, _dropIndicator
    
    private int _lastIndicatorRow = -1;
    private int _lastIndicatorCol = -1;

    #region Item Drag Events

    private void PinnedItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
        
        if (sender is Button button && button.Tag is PinnedItem item)
        {
            _draggedButton = button;
            _draggedItem = item;
            _draggedGroup = null;
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
            _draggedButton.Opacity = 0.3;
            
            var data = new DataObject("PinnedItem", _draggedItem);
            DragDrop.DoDragDrop(_draggedButton, data, DragDropEffects.Move);
            
            if (_draggedButton != null)
                _draggedButton.Opacity = 1.0;
            
            RemoveDropIndicator();
            _isDragging = false;
            _draggedItem = null;
            _draggedButton = null;
        }
    }

    private void PinnedItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedButton != null)
            _draggedButton.Opacity = 1.0;
        
        RemoveDropIndicator();
        _isDragging = false;
        _draggedItem = null;
        _draggedButton = null;
    }

    #endregion

    #region Group Drag Events

    private void GroupFolder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
        
        if (sender is Button button && button.Tag is Group group)
        {
            _draggedButton = button;
            _draggedGroup = group;
            _draggedItem = null;
        }
    }

    private void GroupFolder_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedGroup == null || _draggedButton == null)
            return;

        var currentPos = e.GetPosition(null);
        var diff = _dragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragging = true;
            _draggedButton.Opacity = 0.3;
            
            var data = new DataObject("Group", _draggedGroup);
            DragDrop.DoDragDrop(_draggedButton, data, DragDropEffects.Move);
            
            if (_draggedButton != null)
                _draggedButton.Opacity = 1.0;
            
            RemoveDropIndicator();
            _isDragging = false;
            _draggedGroup = null;
            _draggedButton = null;
        }
    }

    private void GroupFolder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedButton != null)
            _draggedButton.Opacity = 1.0;
        
        RemoveDropIndicator();
        _isDragging = false;
        _draggedGroup = null;
        _draggedButton = null;
    }

    #endregion

    #region Grid Drag Over & Drop

    private void ItemsGrid_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Grid grid) return;
        
        // Accept PinnedItem or Group drag
        if (e.Data.GetDataPresent("PinnedItem") || e.Data.GetDataPresent("Group"))
        {
            e.Effects = DragDropEffects.Move;
            
            var dropPosition = e.GetPosition(grid);
            var cellSize = _settingsService.Settings.ItemSize + 8;
            
            var col = Math.Max(0, (int)(dropPosition.X / cellSize));
            var row = Math.Max(0, (int)(dropPosition.Y / cellSize));
            
            // Clamp column to grid bounds
            col = Math.Min(col, grid.ColumnDefinitions.Count - 1);
            
            // In FreeForm mode, allow expanding rows dynamically
            // In Ordered mode, clamp to existing rows
            if (_settingsService.Settings.PinnedItemsLayout == LayoutMode.Ordered)
            {
                row = Math.Min(row, grid.RowDefinitions.Count - 1);
            }
            else
            {
                // FreeForm mode: add rows dynamically if needed
                while (row >= grid.RowDefinitions.Count)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellSize) });
                }
            }
            
            ShowDropIndicator(grid, row, col, cellSize);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            RemoveDropIndicator();
        }
        
        e.Handled = true;
    }

    private void ItemsGrid_DragLeave(object sender, DragEventArgs e)
    {
        RemoveDropIndicator();
    }

    private void ItemsGrid_Drop(object sender, DragEventArgs e)
    {
        RemoveDropIndicator();
        
        if (sender is not Grid grid) return;
        
        var dropPosition = e.GetPosition(grid);
        var cellSize = _settingsService.Settings.ItemSize + 8;
        
        var targetCol = Math.Max(0, (int)(dropPosition.X / cellSize));
        var targetRow = Math.Max(0, (int)(dropPosition.Y / cellSize));
        
        // Clamp column to grid bounds
        targetCol = Math.Min(targetCol, grid.ColumnDefinitions.Count - 1);
        
        // In FreeForm mode, rows were already expanded during DragOver
        // Just make sure row is within current grid bounds
        targetRow = Math.Min(targetRow, Math.Max(0, grid.RowDefinitions.Count - 1));
        
        var currentTabId = _currentTabId ?? _pinnedItemsService.DefaultTab.Id;
        var gridColumns = CalculateGridColumns();
        
        if (e.Data.GetDataPresent("PinnedItem"))
        {
            var item = e.Data.GetData("PinnedItem") as PinnedItem;
            if (item != null)
            {
                _pinnedItemsService.MoveToCell(item.Id, false, targetRow, targetCol, currentTabId, _openGroupId);
                
                // In Ordered mode, compact after move
                if (_settingsService.Settings.PinnedItemsLayout == LayoutMode.Ordered)
                {
                    _pinnedItemsService.CompactItems(currentTabId, _openGroupId, gridColumns);
                }
            }
        }
        else if (e.Data.GetDataPresent("Group"))
        {
            var group = e.Data.GetData("Group") as Group;
            if (group != null && _openGroupId == null) // Groups only at main level
            {
                _pinnedItemsService.MoveToCell(group.Id, true, targetRow, targetCol, currentTabId, null);
                
                if (_settingsService.Settings.PinnedItemsLayout == LayoutMode.Ordered)
                {
                    _pinnedItemsService.CompactItems(currentTabId, null, gridColumns);
                }
            }
        }
        
        e.Handled = true;
    }

    #endregion

    #region Group Folder Drop (for adding items to group)

    private void GroupFolder_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedItem"))
        {
            var item = e.Data.GetData("PinnedItem") as PinnedItem;
            if (sender is Button btn && btn.Tag is Group group)
            {
                // Don't allow if item is already in this group
                if (item != null && item.GroupId != group.Id)
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
            }
        }
        
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupFolder_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("PinnedItem")) return;
        
        var item = e.Data.GetData("PinnedItem") as PinnedItem;
        if (sender is Button button && button.Tag is Group group)
        {
            if (item != null && item.GroupId == group.Id) return;
            
            // Visual feedback - scale up
            var scaleTransform = new ScaleTransform(1.15, 1.15);
            button.RenderTransform = scaleTransform;
            button.RenderTransformOrigin = new Point(0.5, 0.5);

            var scaleAnimation = new DoubleAnimation(1.0, 1.15, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

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

    private void GroupFolder_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            ResetGroupFolderVisuals(button);
        }

        if (e.Data.GetDataPresent("PinnedItem") && sender is Button btn && btn.Tag is Group group)
        {
            var item = e.Data.GetData("PinnedItem") as PinnedItem;
            if (item != null && item.GroupId != group.Id)
            {
                var gridColumns = CalculateGridColumns();
                _pinnedItemsService.MoveItemToGroup(item.Id, group.Id, gridColumns);
                
                // Compact main grid in Ordered mode
                if (_settingsService.Settings.PinnedItemsLayout == LayoutMode.Ordered)
                {
                    var currentTabId = _currentTabId ?? _pinnedItemsService.DefaultTab.Id;
                    _pinnedItemsService.CompactItems(currentTabId, null, gridColumns);
                }
            }
        }
        
        e.Handled = true;
    }

    private void ResetGroupFolderVisuals(Button button)
    {
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

        button.Effect = null;
    }

    #endregion

    #region Drop Indicator

    private void ShowDropIndicator(Grid grid, int row, int col, double cellSize)
    {
        if (row == _lastIndicatorRow && col == _lastIndicatorCol && _dropIndicator != null)
            return;
        
        _lastIndicatorRow = row;
        _lastIndicatorCol = col;
        
        RemoveDropIndicator();
        
        _dropIndicator = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 120, 212)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(4),
            IsHitTestVisible = false,
            Tag = "DropIndicator"
        };
        
        _dropIndicator.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0x00, 0x78, 0xD4),
            BlurRadius = 15,
            ShadowDepth = 0,
            Opacity = 0.6
        };
        
        Grid.SetRow(_dropIndicator, row);
        Grid.SetColumn(_dropIndicator, col);
        
        grid.Children.Add(_dropIndicator);
    }

    private void RemoveDropIndicator()
    {
        if (_dropIndicator != null && _dropIndicator.Parent is Grid parentGrid)
        {
            parentGrid.Children.Remove(_dropIndicator);
        }
        _dropIndicator = null;
        _lastIndicatorRow = -1;
        _lastIndicatorCol = -1;
    }

    #endregion

    #region Empty Space Context Menu

    private void ItemsGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Check if click was on empty cell
        var hitTest = VisualTreeHelper.HitTest(sender as Visual, e.GetPosition(sender as IInputElement));
        if (hitTest?.VisualHit is Grid || hitTest?.VisualHit is Border border && border.Tag as string == "GridCell")
        {
            ShowEmptySpaceContextMenu(e.GetPosition(sender as IInputElement));
            e.Handled = true;
        }
    }

    private void ShowEmptySpaceContextMenu(Point position)
    {
        var contextMenu = new ContextMenu();

        var createGroupItem = new MenuItem { Header = "Yeni Klasör Oluştur" };
        createGroupItem.Click += (s, e) => ShowCreateGroupDialog();

        contextMenu.Items.Add(createGroupItem);
        contextMenu.IsOpen = true;
    }

    #endregion

    /// <summary>
    /// Calculate number of columns based on available width
    /// </summary>
    private int CalculateGridColumns()
    {
        var cellSize = _settingsService.Settings.ItemSize + 8;
        var availableWidth = PinnedScrollViewer.ActualWidth > 0 ? PinnedScrollViewer.ActualWidth - 16 : 600;
        return Math.Max(1, (int)(availableWidth / cellSize));
    }
}
