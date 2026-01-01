using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CustomStartMenu.Models;

namespace CustomStartMenu.Views;

/// <summary>
/// Drag & Drop functionality for the Start Menu
/// </summary>
public partial class StartMenuWindow
{
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
}
