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
/// Manages pinned items, tabs, and groups - save, load, add, remove
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
        // Ignore changes caused by our own save
        if (_isInternalSave) return;

        // Debounce and reload
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
                
                // Migrate from old format (pinned-items.json)
                var oldConfigPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "pinned-items.json");
                if (_pinnedItems.Count == 0 && File.Exists(oldConfigPath))
                {
                    try
                    {
                        var oldJson = File.ReadAllText(oldConfigPath);
                        _pinnedItems = JsonSerializer.Deserialize<List<PinnedItem>>(oldJson) ?? new();
                        if (_pinnedItems.Count > 0)
                        {
                            Save(); // Save to new format
                        }
                    }
                    catch { }
                }

                // Ensure at least one tab exists
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

                // Small delay to ensure file watcher doesn't pick up our own change
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
        if (tab == null || _tabs.Count <= 1) return; // Keep at least one tab

        // Move items from this tab to default tab
        var defaultTab = _tabs.Where(t => t.Id != tabId).OrderBy(t => t.Order).First();
        foreach (var item in _pinnedItems.Where(p => p.TabId == tabId))
        {
            item.TabId = defaultTab.Id;
        }

        // Remove groups in this tab
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

    public Group AddGroup(string name, string? tabId = null)
    {
        tabId ??= DefaultTab.Id;
        
        // Get the max global order from both ungrouped items and groups in this tab
        var maxGlobalOrder = GetMaxGlobalOrder(tabId);
        
        var groupsInTab = _groups.Where(g => g.TabId == tabId).ToList();
        var group = new Group
        {
            Name = name,
            TabId = tabId,
            Order = groupsInTab.Count,
            GlobalOrder = maxGlobalOrder + 1
        };
        _groups.Add(group);
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        return group;
    }
    
    /// <summary>
    /// Get the maximum GlobalOrder value among ungrouped items and groups in a tab
    /// </summary>
    private int GetMaxGlobalOrder(string? tabId)
    {
        tabId ??= DefaultTab.Id;
        
        var maxItemOrder = _pinnedItems
            .Where(p => p.TabId == tabId && p.GroupId == null)
            .Select(p => p.GlobalOrder)
            .DefaultIfEmpty(-1)
            .Max();
            
        var maxGroupOrder = _groups
            .Where(g => g.TabId == tabId)
            .Select(g => g.GlobalOrder)
            .DefaultIfEmpty(-1)
            .Max();
            
        return Math.Max(maxItemOrder, maxGroupOrder);
    }
    
    /// <summary>
    /// Get all displayable elements (ungrouped items + groups) sorted by GlobalOrder
    /// </summary>
    public List<object> GetSortedElementsForTab(string? tabId)
    {
        tabId ??= DefaultTab.Id;
        
        var elements = new List<(object Element, int GlobalOrder)>();
        
        // Add ungrouped items
        foreach (var item in _pinnedItems.Where(p => p.TabId == tabId && p.GroupId == null))
        {
            elements.Add((item, item.GlobalOrder));
        }
        
        // Add groups
        foreach (var group in _groups.Where(g => g.TabId == tabId))
        {
            elements.Add((group, group.GlobalOrder));
        }
        
        return elements.OrderBy(e => e.GlobalOrder).Select(e => e.Element).ToList();
    }
    
    /// <summary>
    /// Move an element (item or group) to a new global position
    /// </summary>
    public void MoveElementToGlobalPosition(string elementId, bool isGroup, int toGlobalIndex, string? tabId = null)
    {
        tabId ??= DefaultTab.Id;
        
        // Get all elements sorted by current GlobalOrder
        var elements = GetSortedElementsForTab(tabId);
        
        // Find and remove the element being moved
        object? movingElement = null;
        if (isGroup)
        {
            movingElement = _groups.FirstOrDefault(g => g.Id == elementId);
        }
        else
        {
            movingElement = _pinnedItems.FirstOrDefault(p => p.Id == elementId);
        }
        
        if (movingElement == null) return;
        
        elements.Remove(movingElement);
        
        // Clamp the target index
        toGlobalIndex = Math.Max(0, Math.Min(toGlobalIndex, elements.Count));
        
        // Insert at new position
        elements.Insert(toGlobalIndex, movingElement);
        
        // Update GlobalOrder for all elements
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i] is PinnedItem item)
            {
                item.GlobalOrder = i;
            }
            else if (elements[i] is Group group)
            {
                group.GlobalOrder = i;
            }
        }
        
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
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

        // Ungroup items in this group
        foreach (var item in _pinnedItems.Where(p => p.GroupId == groupId))
        {
            item.GroupId = null;
        }

        _groups.Remove(group);
        ReorderGroupsInTab(group.TabId);
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

    /// <summary>
    /// Move a group to a new position within its tab
    /// </summary>
    /// <param name="groupId">The ID of the group to move</param>
    /// <param name="toIndex">The target index position</param>
    public void MoveGroup(string groupId, int toIndex)
    {
        var group = _groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return;

        var groupsInTab = _groups
            .Where(g => g.TabId == group.TabId)
            .OrderBy(g => g.Order)
            .ToList();

        // Remove the group from its current position
        groupsInTab.Remove(group);

        // Clamp the target index
        toIndex = Math.Max(0, Math.Min(toIndex, groupsInTab.Count));

        // Insert at the new position
        groupsInTab.Insert(toIndex, group);

        // Update order values
        for (int i = 0; i < groupsInTab.Count; i++)
        {
            groupsInTab[i].Order = i;
        }

        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReorderGroupsInTab(string? tabId)
    {
        var groupsInTab = _groups.Where(g => g.TabId == tabId).OrderBy(g => g.Order).ToList();
        for (int i = 0; i < groupsInTab.Count; i++)
        {
            groupsInTab[i].Order = i;
        }
    }

    #endregion

    #region Pinned Item Management

    public void AddPin(string path, string? tabId = null, string? groupId = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        
        // Check if already pinned
        if (_pinnedItems.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        var isDirectory = Directory.Exists(path);
        var isFile = File.Exists(path);

        if (!isDirectory && !isFile) return;

        tabId ??= DefaultTab.Id;

        var itemsInContext = _pinnedItems
            .Where(p => p.TabId == tabId && p.GroupId == groupId)
            .ToList();

        // Determine the item type
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

        // Calculate GlobalOrder for ungrouped items
        var globalOrder = 0;
        if (groupId == null)
        {
            globalOrder = GetMaxGlobalOrder(tabId) + 1;
        }

        var item = new PinnedItem
        {
            Path = path,
            Name = Path.GetFileNameWithoutExtension(path) ?? Path.GetFileName(path),
            Type = itemType,
            Order = itemsInContext.Count,
            GlobalOrder = globalOrder,
            TabId = tabId,
            GroupId = groupId
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
            ReorderItemsInContext(item.TabId, item.GroupId);
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
            ReorderItemsInContext(item.TabId, item.GroupId);
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsPinned(string path)
    {
        return _pinnedItems.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Update a pinned item (e.g., after renaming)
    /// </summary>
    public void UpdatePinnedItem(PinnedItem item)
    {
        var existingItem = _pinnedItems.FirstOrDefault(p => p.Id == item.Id);
        if (existingItem != null)
        {
            // Update the properties that can be changed
            existingItem.CustomName = item.CustomName;
            existingItem.GridRow = item.GridRow;
            existingItem.GridColumn = item.GridColumn;
            // Add other updatable properties here as needed
            
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Clear grid positions for all items in a tab (used when switching to Ordered mode)
    /// </summary>
    public void ClearGridPositionsForTab(string? tabId)
    {
        tabId ??= DefaultTab.Id;
        
        var itemsInTab = _pinnedItems.Where(p => p.TabId == tabId).ToList();
        foreach (var item in itemsInTab)
        {
            item.GridRow = null;
            item.GridColumn = null;
        }
        
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Update grid position for a pinned item (used in FreeForm mode)
    /// </summary>
    public void UpdateItemGridPosition(string itemId, int? gridRow, int? gridColumn)
    {
        var item = _pinnedItems.FirstOrDefault(p => p.Id == itemId);
        if (item != null)
        {
            item.GridRow = gridRow;
            item.GridColumn = gridColumn;
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    
    /// <summary>
    /// Update grid position for a group (used in FreeForm mode)
    /// </summary>
    public void UpdateGroupGridPosition(string groupId, int? gridRow, int? gridColumn)
    {
        var group = _groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            group.GridRow = gridRow;
            group.GridColumn = gridColumn;
            Save();
            PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MoveItem(string itemId, int toIndex, string? targetTabId = null, string? targetGroupId = null)
    {
        var item = _pinnedItems.FirstOrDefault(p => p.Id == itemId);
        if (item == null) return;

        var sourceTabId = item.TabId;
        var sourceGroupId = item.GroupId;

        // Update tab and group if moving to different context
        if (targetTabId != null)
            item.TabId = targetTabId;
        if (targetGroupId != null || (targetTabId != null && targetGroupId == null))
            item.GroupId = targetGroupId;

        // Get items in target context
        var itemsInTarget = _pinnedItems
            .Where(p => p.TabId == item.TabId && p.GroupId == item.GroupId && p.Id != item.Id)
            .OrderBy(p => p.Order)
            .ToList();

        // Insert at new position
        toIndex = Math.Max(0, Math.Min(toIndex, itemsInTarget.Count));
        itemsInTarget.Insert(toIndex, item);

        // Reorder
        for (int i = 0; i < itemsInTarget.Count; i++)
        {
            itemsInTarget[i].Order = i;
        }

        // Reorder source context if different
        if (sourceTabId != item.TabId || sourceGroupId != item.GroupId)
        {
            ReorderItemsInContext(sourceTabId, sourceGroupId);
        }

        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _pinnedItems.Count) return;
        if (toIndex < 0 || toIndex >= _pinnedItems.Count) return;

        var item = _pinnedItems[fromIndex];
        _pinnedItems.RemoveAt(fromIndex);
        _pinnedItems.Insert(toIndex, item);
        ReorderItems();
        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveItemToGroup(string itemId, string? groupId)
    {
        var item = _pinnedItems.FirstOrDefault(p => p.Id == itemId);
        if (item == null) return;

        var oldGroupId = item.GroupId;
        item.GroupId = groupId;

        // Reorder old context
        ReorderItemsInContext(item.TabId, oldGroupId);

        // Set order in new context
        var itemsInNewContext = _pinnedItems
            .Where(p => p.TabId == item.TabId && p.GroupId == groupId && p.Id != item.Id)
            .ToList();
        item.Order = itemsInNewContext.Count;

        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveItemToTab(string itemId, string tabId)
    {
        var item = _pinnedItems.FirstOrDefault(p => p.Id == itemId);
        if (item == null) return;

        var oldTabId = item.TabId;
        var oldGroupId = item.GroupId;
        
        item.TabId = tabId;
        item.GroupId = null; // Remove from group when moving to different tab

        // Reorder old context
        ReorderItemsInContext(oldTabId, oldGroupId);

        // Set order in new context
        var itemsInNewContext = _pinnedItems
            .Where(p => p.TabId == tabId && p.GroupId == null && p.Id != item.Id)
            .ToList();
        item.Order = itemsInNewContext.Count;

        Save();
        PinnedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Get items for a specific tab, optionally filtered by group
    /// </summary>
    public IEnumerable<PinnedItem> GetItemsForTab(string? tabId, string? groupId = null)
    {
        tabId ??= DefaultTab.Id;
        
        return _pinnedItems
            .Where(p => p.TabId == tabId && p.GroupId == groupId)
            .OrderBy(p => p.Order);
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
        return _groups.Where(g => g.TabId == tabId).OrderBy(g => g.Order);
    }

    private void ReorderItems()
    {
        for (int i = 0; i < _pinnedItems.Count; i++)
        {
            _pinnedItems[i].Order = i;
        }
    }

    private void ReorderItemsInContext(string? tabId, string? groupId)
    {
        var items = _pinnedItems
            .Where(p => p.TabId == tabId && p.GroupId == groupId)
            .OrderBy(p => p.Order)
            .ToList();

        for (int i = 0; i < items.Count; i++)
        {
            items[i].Order = i;
        }
    }

    #endregion

    public void Dispose()
    {
        _fileWatcher?.Dispose();
    }
}
