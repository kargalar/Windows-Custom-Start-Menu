using System.IO;
using System.Text.Json;
using CustomStartMenu.Models;

namespace CustomStartMenu.Services;

/// <summary>
/// Configuration data for all pinned items, tabs, and groups
/// </summary>
public class PinnedItemsConfig
{
    public List<Tab> Tabs { get; set; } = new();
    public List<Group> Groups { get; set; } = new();
    public List<PinnedItem> Items { get; set; } = new();
}

/// <summary>
/// Manages pinned items, tabs, and groups with grid-based positioning
/// </summary>
public class PinnedItemsService : IDisposable
{
    private readonly string _configPath;
    private List<PinnedItem> _pinnedItems = new();
    private List<Tab> _tabs = new();
    private List<Group> _groups = new();
    private FileSystemWatcher? _fileWatcher;
    private bool _isInternalSave;
    private readonly object _lockObject = new();

    public event EventHandler? PinnedItemsChanged;

    public IReadOnlyList<PinnedItem> PinnedItems => _pinnedItems.AsReadOnly();
    public IReadOnlyList<Tab> Tabs => _tabs.AsReadOnly();
    public IReadOnlyList<Group> Groups => _groups.AsReadOnly();

    /// <summary>
    /// Get the default tab (first tab, creates one if none exist)
    /// </summary>
    public Tab DefaultTab
    {
        get
        {
            if (_tabs.Count == 0)
            {
                var defaultTab = new Tab { Name = "Ana Sayfa", Order = 0 };
                _tabs.Add(defaultTab);
                Save();
            }
            return _tabs.OrderBy(t => t.Order).First();
        }
    }

    public PinnedItemsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomStartMenu");
        
        Directory.CreateDirectory(appDataPath);
        _configPath = Path.Combine(appDataPath, "config.json");
        
        Load();
        SetupFileWatcher(appDataPath);
    }

    private void SetupFileWatcher(string directoryPath)
    {
        try
        {
            _fileWatcher = new FileSystemWatcher(directoryPath, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnConfigFileChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to setup file watcher: {ex.Message}");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_isInternalSave) return;

        Task.Delay(100).ContinueWith(_ =>
        {
            lock (_lockObject)
            {
                if (_isInternalSave) return;
                
                Load();
                PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public void Load()
    {
        lock (_lockObject)
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<PinnedItemsConfig>(json);
                    
                    if (config != null)
                    {
                        _tabs = config.Tabs ?? new();
                        _groups = config.Groups ?? new();
                        _pinnedItems = config.Items ?? new();
                    }
                }
                
                // Migrate from old format
                var oldConfigPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "pinned-items.json");
                if (_pinnedItems.Count == 0 && File.Exists(oldConfigPath))
                {
                    try
                    {
                        var oldJson = File.ReadAllText(oldConfigPath);
                        _pinnedItems = JsonSerializer.Deserialize<List<PinnedItem>>(oldJson) ?? new();
                        if (_pinnedItems.Count > 0)
                        {
                            // Assign grid positions to migrated items
                            AssignGridPositionsToItems(_pinnedItems, null, 10);
                            Save();
                        }
                    }
                    catch { }
                }

                if (_tabs.Count == 0)
                {
                    _tabs.Add(new Tab { Name = "Ana Sayfa", Order = 0 });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
                _pinnedItems = new();
                _tabs = new() { new Tab { Name = "Ana Sayfa", Order = 0 } };
                _groups = new();
            }
        }
    }

    public void Save()
    {
        lock (_lockObject)
        {
            try
            {
                _isInternalSave = true;

                var config = new PinnedItemsConfig
                {
                    Tabs = _tabs,
                    Groups = _groups,
                    Items = _pinnedItems
                };

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_configPath, json);

                Task.Delay(200).ContinueWith(_ => _isInternalSave = false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
                _isInternalSave = false;
            }
        }
    }

    #region Tab Management

    public Tab AddTab(string name)
    {
        var tab = new Tab
        {
            Name = name,
            Order = _tabs.Count
        };
        _tabs.Add(tab);
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        return tab;
    }

    public void RenameTab(string tabId, string newName)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab != null)
        {
            tab.Name = newName;
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveTab(string tabId)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || _tabs.Count <= 1) return;

        var defaultTab = _tabs.Where(t => t.Id != tabId).OrderBy(t => t.Order).First();
        foreach (var item in _pinnedItems.Where(p => p.TabId == tabId))
        {
            item.TabId = defaultTab.Id;
        }

        _groups.RemoveAll(g => g.TabId == tabId);
        _tabs.Remove(tab);
        ReorderTabs();
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveTab(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _tabs.Count) return;
        if (toIndex < 0 || toIndex >= _tabs.Count) return;

        var orderedTabs = _tabs.OrderBy(t => t.Order).ToList();
        var tab = orderedTabs[fromIndex];
        orderedTabs.RemoveAt(fromIndex);
        orderedTabs.Insert(toIndex, tab);

        for (int i = 0; i < orderedTabs.Count; i++)
        {
            orderedTabs[i].Order = i;
        }

        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReorderTabs()
    {
        var orderedTabs = _tabs.OrderBy(t => t.Order).ToList();
        for (int i = 0; i < orderedTabs.Count; i++)
        {
            orderedTabs[i].Order = i;
        }
    }

    #endregion

    #region Group Management

    public Group AddGroup(string name, string? tabId, int gridColumns)
    {
        tabId ??= DefaultTab.Id;
        
        var (row, col) = GetNextAvailableCell(tabId, null, gridColumns);
        
        var group = new Group
        {
            Name = name,
            TabId = tabId,
            GridRow = row,
            GridColumn = col
        };
        _groups.Add(group);
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        return group;
    }

    public void RenameGroup(string groupId, string newName)
    {
        var group = _groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            group.Name = newName;
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveGroup(string groupId)
    {
        var group = _groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return;

        // Ungroup items - place them at available positions
        var itemsInGroup = _pinnedItems.Where(p => p.GroupId == groupId).ToList();
        foreach (var item in itemsInGroup)
        {
            item.GroupId = null;
        }

        _groups.Remove(group);
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleGroupExpanded(string groupId)
    {
        var group = _groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            group.IsExpanded = !group.IsExpanded;
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion

    #region Grid-Based Item Management

    /// <summary>
    /// Get the next available cell in the grid
    /// </summary>
    public (int row, int col) GetNextAvailableCell(string? tabId, string? groupId, int gridColumns)
    {
        tabId ??= DefaultTab.Id;
        
        var occupiedCells = GetOccupiedCells(tabId, groupId);
        
        int row = 0, col = 0;
        while (occupiedCells.Contains((row, col)))
        {
            col++;
            if (col >= gridColumns)
            {
                col = 0;
                row++;
            }
        }
        
        return (row, col);
    }

    /// <summary>
    /// Get all occupied cells in a tab (or group)
    /// </summary>
    public HashSet<(int row, int col)> GetOccupiedCells(string? tabId, string? groupId)
    {
        tabId ??= DefaultTab.Id;
        var occupied = new HashSet<(int row, int col)>();
        
        if (groupId == null)
        {
            // Main tab view - check ungrouped items and groups
            foreach (var item in _pinnedItems.Where(p => p.TabId == tabId && p.GroupId == null))
            {
                occupied.Add((item.GridRow, item.GridColumn));
            }
            foreach (var group in _groups.Where(g => g.TabId == tabId))
            {
                occupied.Add((group.GridRow, group.GridColumn));
            }
        }
        else
        {
            // Inside a group - only check items in that group
            foreach (var item in _pinnedItems.Where(p => p.GroupId == groupId))
            {
                occupied.Add((item.GridRow, item.GridColumn));
            }
        }
        
        return occupied;
    }

    /// <summary>
    /// Check if a cell is occupied
    /// </summary>
    public bool IsCellOccupied(string? tabId, string? groupId, int row, int col)
    {
        return GetOccupiedCells(tabId, groupId).Contains((row, col));
    }

    /// <summary>
    /// Get element at a specific cell (returns PinnedItem, Group, or null)
    /// </summary>
    public object? GetElementAtCell(string? tabId, string? groupId, int row, int col)
    {
        tabId ??= DefaultTab.Id;
        
        if (groupId == null)
        {
            // Check ungrouped items
            var item = _pinnedItems.FirstOrDefault(p => 
                p.TabId == tabId && p.GroupId == null && p.GridRow == row && p.GridColumn == col);
            if (item != null) return item;
            
            // Check groups
            var group = _groups.FirstOrDefault(g => 
                g.TabId == tabId && g.GridRow == row && g.GridColumn == col);
            return group;
        }
        else
        {
            // Inside a group - only items
            return _pinnedItems.FirstOrDefault(p => 
                p.GroupId == groupId && p.GridRow == row && p.GridColumn == col);
        }
    }

    /// <summary>
    /// Move an element to a cell - swaps if occupied
    /// </summary>
    public void MoveToCell(string elementId, bool isGroup, int targetRow, int targetCol, string? tabId, string? groupId)
    {
        tabId ??= DefaultTab.Id;
        
        if (isGroup)
        {
            var group = _groups.FirstOrDefault(g => g.Id == elementId);
            if (group == null) return;
            
            var sourceRow = group.GridRow;
            var sourceCol = group.GridColumn;
            
            // Check if target is occupied
            var targetElement = GetElementAtCell(tabId, null, targetRow, targetCol);
            
            if (targetElement != null)
            {
                // Swap positions
                if (targetElement is PinnedItem targetItem)
                {
                    targetItem.GridRow = sourceRow;
                    targetItem.GridColumn = sourceCol;
                }
                else if (targetElement is Group targetGroup)
                {
                    targetGroup.GridRow = sourceRow;
                    targetGroup.GridColumn = sourceCol;
                }
            }
            
            group.GridRow = targetRow;
            group.GridColumn = targetCol;
        }
        else
        {
            var item = _pinnedItems.FirstOrDefault(p => p.Id == elementId);
            if (item == null) return;
            
            var sourceRow = item.GridRow;
            var sourceCol = item.GridColumn;
            var sourceGroupId = item.GroupId;
            
            // If moving to a different group context
            if (groupId != sourceGroupId)
            {
                item.GroupId = groupId;
                // Reset position in new context
                var (newRow, newCol) = GetNextAvailableCell(tabId, groupId, 10);
                item.GridRow = newRow;
                item.GridColumn = newCol;
            }
            else
            {
                // Same context - check for swap
                var targetElement = GetElementAtCell(tabId, groupId, targetRow, targetCol);
                
                if (targetElement != null)
                {
                    // Swap positions
                    if (targetElement is PinnedItem targetItem)
                    {
                        targetItem.GridRow = sourceRow;
                        targetItem.GridColumn = sourceCol;
                    }
                    else if (targetElement is Group targetGroup && groupId == null)
                    {
                        targetGroup.GridRow = sourceRow;
                        targetGroup.GridColumn = sourceCol;
                    }
                }
                
                item.GridRow = targetRow;
                item.GridColumn = targetCol;
            }
        }
        
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Move item to a group (folder)
    /// </summary>
    public void MoveItemToGroup(string itemId, string? targetGroupId, int gridColumns)
    {
        var item = _pinnedItems.FirstOrDefault(p => p.Id == itemId);
        if (item == null) return;
        
        var oldGroupId = item.GroupId;
        if (oldGroupId == targetGroupId) return;
        
        item.GroupId = targetGroupId;
        
        // Assign new grid position in target context
        var tabId = item.TabId ?? DefaultTab.Id;
        var (row, col) = GetNextAvailableCell(tabId, targetGroupId, gridColumns);
        item.GridRow = row;
        item.GridColumn = col;
        
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Move item to a different tab
    /// </summary>
    public void MoveItemToTab(string itemId, string tabId, int gridColumns)
    {
        var item = _pinnedItems.FirstOrDefault(p => p.Id == itemId);
        if (item == null) return;
        
        item.TabId = tabId;
        item.GroupId = null;
        
        var (row, col) = GetNextAvailableCell(tabId, null, gridColumns);
        item.GridRow = row;
        item.GridColumn = col;
        
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Compact all items in a tab (remove gaps) - used for Ordered mode
    /// </summary>
    /// <param name="fireEvent">If false, won't trigger PinnedItemsChanged event (prevents infinite loops during render)</param>
    public void CompactItems(string? tabId, string? groupId, int gridColumns, bool fireEvent = true)
    {
        tabId ??= DefaultTab.Id;
        
        List<object> elements = new();
        
        if (groupId == null)
        {
            // Get all ungrouped items and groups, sorted by current position
            var items = _pinnedItems
                .Where(p => p.TabId == tabId && p.GroupId == null)
                .OrderBy(p => p.GridRow)
                .ThenBy(p => p.GridColumn)
                .Cast<object>();
            var groups = _groups
                .Where(g => g.TabId == tabId)
                .OrderBy(g => g.GridRow)
                .ThenBy(g => g.GridColumn)
                .Cast<object>();
            
            elements = items.Concat(groups).ToList();
        }
        else
        {
            elements = _pinnedItems
                .Where(p => p.GroupId == groupId)
                .OrderBy(p => p.GridRow)
                .ThenBy(p => p.GridColumn)
                .Cast<object>()
                .ToList();
        }
        
        // Reassign positions sequentially
        int row = 0, col = 0;
        foreach (var element in elements)
        {
            if (element is PinnedItem item)
            {
                item.GridRow = row;
                item.GridColumn = col;
            }
            else if (element is Group group)
            {
                group.GridRow = row;
                group.GridColumn = col;
            }
            
            col++;
            if (col >= gridColumns)
            {
                col = 0;
                row++;
            }
        }
        
        Save();
        if (fireEvent)
        {
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Helper to assign grid positions to a list of items
    /// </summary>
    private void AssignGridPositionsToItems(List<PinnedItem> items, string? groupId, int gridColumns)
    {
        int row = 0, col = 0;
        foreach (var item in items.Where(i => i.GroupId == groupId))
        {
            item.GridRow = row;
            item.GridColumn = col;
            col++;
            if (col >= gridColumns)
            {
                col = 0;
                row++;
            }
        }
    }

    #endregion

    #region Pinned Item Management

    public void AddPin(string path, string? tabId, string? groupId, int gridColumns)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        
        if (_pinnedItems.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        var isDirectory = Directory.Exists(path);
        var isFile = File.Exists(path);

        if (!isDirectory && !isFile) return;

        tabId ??= DefaultTab.Id;

        PinnedItemType itemType;
        if (isDirectory)
        {
            itemType = PinnedItemType.Folder;
        }
        else if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            itemType = PinnedItemType.InternetShortcut;
        }
        else
        {
            itemType = PinnedItemType.Application;
        }

        var (row, col) = GetNextAvailableCell(tabId, groupId, gridColumns);

        var item = new PinnedItem
        {
            Path = path,
            Name = Path.GetFileNameWithoutExtension(path) ?? Path.GetFileName(path),
            Type = itemType,
            TabId = tabId,
            GroupId = groupId,
            GridRow = row,
            GridColumn = col
        };

        _pinnedItems.Add(item);
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemovePin(string path)
    {
        var item = _pinnedItems.FirstOrDefault(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            _pinnedItems.Remove(item);
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemovePinById(string id)
    {
        var item = _pinnedItems.FirstOrDefault(p => p.Id == id);
        if (item != null)
        {
            _pinnedItems.Remove(item);
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsPinned(string path)
    {
        return _pinnedItems.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    public void UpdatePinnedItem(PinnedItem item)
    {
        var existingItem = _pinnedItems.FirstOrDefault(p => p.Id == item.Id);
        if (existingItem != null)
        {
            existingItem.CustomName = item.CustomName;
            existingItem.GridRow = item.GridRow;
            existingItem.GridColumn = item.GridColumn;
            
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Get items for a specific tab, optionally filtered by group
    /// </summary>
    public IEnumerable<PinnedItem> GetItemsForTab(string? tabId, string? groupId = null)
    {
        tabId ??= DefaultTab.Id;
        
        return _pinnedItems
            .Where(p => p.TabId == tabId && p.GroupId == groupId)
            .OrderBy(p => p.GridRow)
            .ThenBy(p => p.GridColumn);
    }

    /// <summary>
    /// Get all ungrouped items for a tab
    /// </summary>
    public IEnumerable<PinnedItem> GetUngroupedItemsForTab(string? tabId)
    {
        tabId ??= DefaultTab.Id;
        return GetItemsForTab(tabId, null);
    }

    /// <summary>
    /// Get groups for a specific tab
    /// </summary>
    public IEnumerable<Group> GetGroupsForTab(string? tabId)
    {
        tabId ??= DefaultTab.Id;
        return _groups
            .Where(g => g.TabId == tabId)
            .OrderBy(g => g.GridRow)
            .ThenBy(g => g.GridColumn);
    }

    /// <summary>
    /// Get all elements (items + groups) for a tab, sorted by grid position
    /// </summary>
    public List<object> GetAllElementsForTab(string? tabId)
    {
        tabId ??= DefaultTab.Id;
        
        var elements = new List<(object element, int row, int col)>();
        
        foreach (var item in _pinnedItems.Where(p => p.TabId == tabId && p.GroupId == null))
        {
            elements.Add((item, item.GridRow, item.GridColumn));
        }
        
        foreach (var group in _groups.Where(g => g.TabId == tabId))
        {
            elements.Add((group, group.GridRow, group.GridColumn));
        }
        
        return elements
            .OrderBy(e => e.row)
            .ThenBy(e => e.col)
            .Select(e => e.element)
            .ToList();
    }

    #endregion

    public void Dispose()
    {
        _fileWatcher?.Dispose();
    }
}
