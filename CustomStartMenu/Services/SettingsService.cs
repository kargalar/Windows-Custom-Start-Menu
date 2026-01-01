using System.IO;
using System.Text.Json;
using CustomStartMenu.Models;

namespace CustomStartMenu.Services;

/// <summary>
/// Manages application settings - load, save, and update with change notifications
/// </summary>
public class SettingsService : IDisposable
{
    private readonly string _settingsPath;
    private AppSettings _settings;
    private FileSystemWatcher? _fileWatcher;
    private bool _isInternalSave;
    private readonly object _lockObject = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Event raised when any setting changes
    /// </summary>
    public event EventHandler? SettingsChanged;

    /// <summary>
    /// Current application settings (read-only access)
    /// </summary>
    public AppSettings Settings => _settings;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CustomStartMenu");

        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");

        _settings = new AppSettings();
        Load();
        SetupFileWatcher(appDataPath);
    }

    private void SetupFileWatcher(string directoryPath)
    {
        try
        {
            _fileWatcher = new FileSystemWatcher(directoryPath, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnSettingsFileChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to setup settings file watcher: {ex.Message}");
        }
    }

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs e)
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
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <summary>
    /// Load settings from disk. If file doesn't exist or is corrupted, uses defaults.
    /// </summary>
    public void Load()
    {
        lock (_lockObject)
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

                    if (loaded != null)
                    {
                        _settings = loaded;
                        ValidateSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                // Keep default settings on error
                _settings = new AppSettings();
            }
        }
    }

    /// <summary>
    /// Save current settings to disk
    /// </summary>
    public void Save()
    {
        lock (_lockObject)
        {
            try
            {
                _isInternalSave = true;

                var json = JsonSerializer.Serialize(_settings, JsonOptions);
                File.WriteAllText(_settingsPath, json);

                // Small delay to ensure file watcher doesn't pick up our own change
                Task.Delay(200).ContinueWith(_ => _isInternalSave = false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
                _isInternalSave = false;
            }
        }
    }

    /// <summary>
    /// Update a specific setting by property name
    /// </summary>
    /// <typeparam name="T">Type of the setting value</typeparam>
    /// <param name="propertyName">Name of the property to update</param>
    /// <param name="value">New value for the setting</param>
    public void UpdateSetting<T>(string propertyName, T value)
    {
        lock (_lockObject)
        {
            var property = typeof(AppSettings).GetProperty(propertyName);
            if (property == null)
            {
                System.Diagnostics.Debug.WriteLine($"Unknown setting: {propertyName}");
                return;
            }

            try
            {
                property.SetValue(_settings, value);
                ValidateSettings();
                Save();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update setting {propertyName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Validate and clamp settings to valid ranges
    /// </summary>
    private void ValidateSettings()
    {
        // Clamp transparency to valid range
        _settings.MenuTransparency = Math.Clamp(_settings.MenuTransparency, 0.0, 1.0);

        // Ensure WebSearchUrl is not null or empty
        if (string.IsNullOrWhiteSpace(_settings.WebSearchUrl))
        {
            _settings.WebSearchUrl = "https://www.google.com/search?q=";
        }

        // Clamp item size (40-150 pixels)
        _settings.ItemSize = Math.Clamp(_settings.ItemSize, 40, 150);

        // Clamp custom dimensions
        _settings.CustomWidth = Math.Clamp(_settings.CustomWidth, 400, 2000);
        _settings.CustomHeight = Math.Clamp(_settings.CustomHeight, 400, 2000);

        // Ensure hotkey config exists
        _settings.OpenMenuHotkey ??= new HotkeyConfig();

        // Ensure accent color is valid hex
        if (string.IsNullOrWhiteSpace(_settings.AccentColor) || !_settings.AccentColor.StartsWith("#"))
        {
            _settings.AccentColor = "#0078D4";
        }
        
        // Clamp background darkness
        _settings.BackgroundDarkness = Math.Clamp(_settings.BackgroundDarkness, 0, 80);
    }

    public void Dispose()
    {
        _fileWatcher?.Dispose();
    }
}
