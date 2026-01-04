using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomStartMenu.Models;
using CustomStartMenu.Services;
using Microsoft.Win32;

namespace CustomStartMenu.Views;

/// <summary>
/// Settings panel functionality for the Start Menu
/// </summary>
public partial class StartMenuWindow
{
    // Hotkey assignment state
    private bool _isAssigningHotkey = false;
    private HotkeyConfig? _pendingHotkey = null;

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
            
            // When switching from FreeForm to Ordered, compact all items
            if (previousMode == LayoutMode.FreeForm && mode == LayoutMode.Ordered)
            {
                var gridColumns = CalculateGridColumns();
                foreach (var tab in _pinnedItemsService.Tabs)
                {
                    // Compact items for main tab view (groupId = null)
                    _pinnedItemsService.CompactItems(tab.Id, null, gridColumns);
                    // Compact items within each group
                    foreach (var group in _pinnedItemsService.GetGroupsForTab(tab.Id))
                    {
                        _pinnedItemsService.CompactItems(tab.Id, group.Id, gridColumns);
                    }
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

    private void ClearAllPinsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Tüm sekmeler, gruplar ve sabitlenmiş öğeler silinecek.\n\nDevam etmek istiyor musunuz?",
            "Tüm Pinleri Temizle",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            _pinnedItemsService.ClearAllPins();
            _currentTabId = _pinnedItemsService.DefaultTab.Id;
            RefreshTabs();
            RefreshPinnedItems();
            
            MessageBox.Show(
                "Tüm sabitlenmiş öğeler temizlendi.",
                "Temizlendi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void ClearAllDataButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Tüm veriler (ayarlar ve sabitlenmiş öğeler) silinecek ve uygulama sıfırlanacak.\n\nDevam etmek istiyor musunuz?",
            "Tüm Verileri Sil",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            // Clear all pins first
            _pinnedItemsService.ClearAllPins();
            
            // Reset settings to defaults
            _settingsService.ResetToDefaults();
            
            // Update current tab
            _currentTabId = _pinnedItemsService.DefaultTab.Id;
            
            // Refresh UI
            RefreshTabs();
            RefreshPinnedItems();
            LoadSettingsIntoControls();
            ApplyTransparency();
            PositionWindow();
            App.Instance.ApplyThemeColor(_settingsService.Settings.AccentColor);
            
            MessageBox.Show(
                "Tüm veriler temizlendi ve uygulama sıfırlandı.",
                "Sıfırlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void ExportDataButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Verileri Dışa Aktar",
                Filter = "JSON dosyası (*.json)|*.json",
                FileName = $"CustomStartMenu_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                DefaultExt = ".json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var exportData = new ExportData
                {
                    ExportDate = DateTime.Now,
                    Version = "1.0",
                    Settings = _settingsService.Settings,
                    PinnedItemsConfig = new PinnedItemsConfig
                    {
                        Tabs = _pinnedItemsService.Tabs.ToList(),
                        Groups = _pinnedItemsService.Groups.ToList(),
                        Items = _pinnedItemsService.PinnedItems.ToList()
                    }
                };

                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                File.WriteAllText(saveDialog.FileName, json);

                MessageBox.Show(
                    $"Veriler başarıyla dışa aktarıldı:\n{saveDialog.FileName}",
                    "Dışa Aktarma Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Dışa aktarma sırasında bir hata oluştu:\n{ex.Message}",
                "Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ImportDataButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openDialog = new OpenFileDialog
            {
                Title = "Verileri İçe Aktar",
                Filter = "JSON dosyası (*.json)|*.json",
                DefaultExt = ".json"
            };

            if (openDialog.ShowDialog() == true)
            {
                var result = MessageBox.Show(
                    "Mevcut tüm veriler içe aktarılan verilerle değiştirilecek.\n\nDevam etmek istiyor musunuz?",
                    "İçe Aktarmayı Onayla",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                var json = File.ReadAllText(openDialog.FileName);
                var importData = JsonSerializer.Deserialize<ExportData>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (importData == null)
                {
                    MessageBox.Show(
                        "Dosya okunamadı veya geçersiz format.",
                        "Hata",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Import settings
                if (importData.Settings != null)
                {
                    _settingsService.ImportSettings(importData.Settings);
                }

                // Import pinned items
                if (importData.PinnedItemsConfig != null)
                {
                    _pinnedItemsService.ImportConfig(importData.PinnedItemsConfig);
                }

                // Update current tab
                _currentTabId = _pinnedItemsService.DefaultTab.Id;

                // Refresh UI
                RefreshTabs();
                RefreshPinnedItems();
                LoadSettingsIntoControls();
                ApplyTransparency();
                PositionWindow();
                App.Instance.ApplyThemeColor(_settingsService.Settings.AccentColor);

                MessageBox.Show(
                    "Veriler başarıyla içe aktarıldı.",
                    "İçe Aktarma Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"İçe aktarma sırasında bir hata oluştu:\n{ex.Message}",
                "Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

/// <summary>
/// Data structure for export/import
/// </summary>
public class ExportData
{
    public DateTime ExportDate { get; set; }
    public string Version { get; set; } = "1.0";
    public AppSettings? Settings { get; set; }
    public PinnedItemsConfig? PinnedItemsConfig { get; set; }
}
