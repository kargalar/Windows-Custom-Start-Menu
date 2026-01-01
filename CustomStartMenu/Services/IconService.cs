using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CustomStartMenu.Services;

/// <summary>
/// Service for extracting and caching file/folder icons
/// </summary>
public class IconService
{
    private static readonly Lazy<IconService> _instance = new(() => new IconService());
    public static IconService Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _extensionCache = new();

    // Default icons
    private ImageSource? _defaultFileIcon;
    private ImageSource? _defaultFolderIcon;
    private ImageSource? _defaultAppIcon;

    #region Win32 API

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    #endregion

    private IconService()
    {
        InitializeDefaultIcons();
    }

    private void InitializeDefaultIcons()
    {
        try
        {
            // Get default folder icon
            _defaultFolderIcon = GetIconFromShell("folder", true);
            
            // Get default file icon
            _defaultFileIcon = GetIconFromShell(".txt", false);
            
            // Get default app icon
            _defaultAppIcon = GetIconFromShell(".exe", false);
        }
        catch
        {
            // Fallback handled by GetIcon methods
        }
    }

    /// <summary>
    /// Get icon for a file or folder path
    /// </summary>
    public ImageSource? GetIcon(string path, bool largeIcon = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            return _defaultFileIcon;

        // Check cache first
        var cacheKey = $"{path}_{(largeIcon ? "L" : "S")}";
        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
            return cachedIcon;

        ImageSource? icon = null;

        try
        {
            if (Directory.Exists(path))
            {
                icon = GetFolderIcon(path, largeIcon);
            }
            else if (File.Exists(path))
            {
                icon = GetFileIcon(path, largeIcon);
            }
            else
            {
                // File doesn't exist, try to get icon by extension
                var ext = Path.GetExtension(path);
                if (!string.IsNullOrEmpty(ext))
                {
                    icon = GetIconByExtension(ext, largeIcon);
                }
            }
        }
        catch
        {
            // Use default
        }

        icon ??= _defaultFileIcon;
        _iconCache[cacheKey] = icon;
        return icon;
    }

    /// <summary>
    /// Get icon by file extension (for search results without actual file access)
    /// </summary>
    public ImageSource? GetIconByExtension(string extension, bool largeIcon = true)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return _defaultFileIcon;

        if (!extension.StartsWith('.'))
            extension = "." + extension;

        var cacheKey = $"ext_{extension}_{(largeIcon ? "L" : "S")}";
        if (_extensionCache.TryGetValue(cacheKey, out var cachedIcon))
            return cachedIcon;

        var icon = GetIconFromShell(extension, false, largeIcon);
        icon ??= _defaultFileIcon;
        
        _extensionCache[cacheKey] = icon;
        return icon;
    }

    /// <summary>
    /// Get default folder icon
    /// </summary>
    public ImageSource? GetDefaultFolderIcon() => _defaultFolderIcon;

    /// <summary>
    /// Get default application icon
    /// </summary>
    public ImageSource? GetDefaultAppIcon() => _defaultAppIcon;

    /// <summary>
    /// Get default file icon
    /// </summary>
    public ImageSource? GetDefaultFileIcon() => _defaultFileIcon;

    /// <summary>
    /// Get icon for an internet shortcut (.url file)
    /// Tries to extract the icon specified in the .url file, falls back to default browser icon
    /// </summary>
    public ImageSource? GetInternetShortcutIcon(string urlFilePath, bool largeIcon = true)
    {
        if (string.IsNullOrWhiteSpace(urlFilePath) || !File.Exists(urlFilePath))
            return GetDefaultBrowserIcon(largeIcon);

        var cacheKey = $"url_{urlFilePath}_{(largeIcon ? "L" : "S")}";
        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
            return cachedIcon;

        ImageSource? icon = null;

        try
        {
            // Try to read IconFile from the .url file
            var lines = File.ReadAllLines(urlFilePath);
            string? iconFile = null;
            int iconIndex = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                {
                    iconFile = line.Substring("IconFile=".Length).Trim();
                }
                else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(line.Substring("IconIndex=".Length).Trim(), out iconIndex);
                }
            }

            // If IconFile is specified and exists, extract icon from it
            if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
            {
                icon = GetFileIcon(iconFile, largeIcon);
            }

            // If no icon found, try to get icon from the .url file itself via shell
            if (icon == null)
            {
                icon = GetIconFromShell(urlFilePath, false, largeIcon);
            }
        }
        catch
        {
            // Fall back to default browser icon
        }

        icon ??= GetDefaultBrowserIcon(largeIcon);
        _iconCache[cacheKey] = icon;
        return icon;
    }

    /// <summary>
    /// Get the default browser icon
    /// </summary>
    public ImageSource? GetDefaultBrowserIcon(bool largeIcon = true)
    {
        var cacheKey = $"browser_{(largeIcon ? "L" : "S")}";
        if (_extensionCache.TryGetValue(cacheKey, out var cachedIcon))
            return cachedIcon;

        // Try to get icon for .html files (associated with default browser)
        var icon = GetIconFromShell(".html", false, largeIcon);
        icon ??= GetIconFromShell(".htm", false, largeIcon);
        icon ??= _defaultAppIcon;

        _extensionCache[cacheKey] = icon;
        return icon;
    }

    private ImageSource? GetFileIcon(string filePath, bool largeIcon)
    {
        try
        {
            // Try to extract the actual icon from the file
            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon != null)
            {
                return ConvertIconToImageSource(icon);
            }
        }
        catch
        {
            // Fall back to shell icon
        }

        return GetIconFromShell(filePath, false, largeIcon);
    }

    private ImageSource? GetFolderIcon(string folderPath, bool largeIcon)
    {
        return GetIconFromShell(folderPath, true, largeIcon);
    }

    private ImageSource? GetIconFromShell(string path, bool isDirectory, bool largeIcon = true)
    {
        var shFileInfo = new SHFILEINFO();
        var flags = SHGFI_ICON | (largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);

        // If path is just an extension, use SHGFI_USEFILEATTRIBUTES
        if (path.StartsWith('.') || !Path.IsPathRooted(path))
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        var fileAttributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        var result = SHGetFileInfo(
            path,
            fileAttributes,
            ref shFileInfo,
            (uint)Marshal.SizeOf(shFileInfo),
            flags);

        if (result == IntPtr.Zero || shFileInfo.hIcon == IntPtr.Zero)
            return null;

        try
        {
            using var icon = Icon.FromHandle(shFileInfo.hIcon);
            return ConvertIconToImageSource(icon);
        }
        finally
        {
            DestroyIcon(shFileInfo.hIcon);
        }
    }

    private static ImageSource? ConvertIconToImageSource(Icon icon)
    {
        try
        {
            var bitmap = icon.ToBitmap();
            var hBitmap = bitmap.GetHbitmap();

            try
            {
                var imageSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                imageSource.Freeze(); // Important for cross-thread access
                return imageSource;
            }
            finally
            {
                DeleteObject(hBitmap);
                bitmap.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Clear the icon cache
    /// </summary>
    public void ClearCache()
    {
        _iconCache.Clear();
        _extensionCache.Clear();
    }
}
