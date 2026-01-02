using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomStartMenu.Models;
using CustomStartMenu.Services;

namespace CustomStartMenu.Views;

/// <summary>
/// Search functionality for the Start Menu
/// </summary>
public partial class StartMenuWindow
{
    private void SwitchToSearchView()
    {
        _isInSearchMode = true;
        _selectedSearchIndex = -1; // Reset selection
        PinnedItemsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        SearchView.Visibility = Visibility.Visible;
        SearchBox.Clear();
        SearchBox.Focus();
        SearchResultsPanel.Items.Clear();
        SearchingText.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        
        // Hide tabs and show close button header with Arama title
        TabBarPanel.Visibility = Visibility.Collapsed;
        CloseButtonHeader.Visibility = Visibility.Visible;
        ViewTitleText.Text = "Arama";
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;

        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResultsPanel.Items.Clear();
            SearchingText.Visibility = Visibility.Collapsed;
            NoResultsText.Visibility = Visibility.Collapsed;
            return;
        }

        // Cancel previous search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        SearchingText.Visibility = Visibility.Visible;
        NoResultsText.Visibility = Visibility.Collapsed;

        try
        {
            // Small delay for debouncing
            await Task.Delay(150, token);

            if (token.IsCancellationRequested) return;

            SearchingText.Visibility = Visibility.Collapsed;
            SearchResultsPanel.Items.Clear();

            // Reset selection when search changes
            _selectedSearchIndex = -1;

            // Check if query is a math expression first
            if (MathEvaluator.TryEvaluate(query, out var mathResult))
            {
                var formattedResult = MathEvaluator.FormatResult(mathResult);
                var calcResult = new SearchResult
                {
                    Name = $"{query} = {formattedResult}",
                    Path = formattedResult,
                    Type = SearchResultType.Calculation,
                    Score = 1000 // Highest score to ensure it's first
                };
                var calcButton = CreateSearchResultButton(calcResult);
                SearchResultsPanel.Items.Add(calcButton);
            }

            // Then search for applications
            var results = await _searchService.SearchAsync(query, token);

            if (token.IsCancellationRequested) return;

            if (results.Count == 0 && SearchResultsPanel.Items.Count == 0)
            {
                NoResultsText.Visibility = Visibility.Visible;
                return;
            }

            foreach (var result in results)
            {
                var button = CreateSearchResultButton(result);
                SearchResultsPanel.Items.Add(button);
            }
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Search error: {ex.Message}");
            SearchingText.Visibility = Visibility.Collapsed;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var itemCount = SearchResultsPanel.Items.Count;

        if (e.Key == Key.Down)
        {
            // Move selection down
            if (itemCount > 0)
            {
                _selectedSearchIndex++;
                if (_selectedSearchIndex >= itemCount)
                {
                    _selectedSearchIndex = 0; // Wrap to first
                }
                UpdateSearchResultSelection();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_selectedSearchIndex == -1 && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                // No selection yet and user pressed Up - trigger web search
                OpenWebSearch(SearchBox.Text);
                e.Handled = true;
            }
            else if (itemCount > 0)
            {
                // Move selection up
                _selectedSearchIndex--;
                if (_selectedSearchIndex < 0)
                {
                    _selectedSearchIndex = itemCount - 1; // Wrap to last
                }
                UpdateSearchResultSelection();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter)
        {
            // Launch selected item or first result
            LaunchSelectedSearchResult();
            e.Handled = true;
        }
    }

    private void UpdateSearchResultSelection()
    {
        for (int i = 0; i < SearchResultsPanel.Items.Count; i++)
        {
            if (SearchResultsPanel.Items[i] is Button button)
            {
                if (i == _selectedSearchIndex)
                {
                    // Apply selected style
                    button.Style = (Style)FindResource("SearchResultSelectedStyle");
                }
                else
                {
                    // Apply normal style
                    button.Style = (Style)FindResource("SearchResultStyle");
                }
            }
        }
    }

    private void LaunchSelectedSearchResult()
    {
        if (SearchResultsPanel.Items.Count == 0) return;

        // If no selection, use first result
        var indexToLaunch = _selectedSearchIndex >= 0 ? _selectedSearchIndex : 0;

        if (indexToLaunch < SearchResultsPanel.Items.Count && 
            SearchResultsPanel.Items[indexToLaunch] is Button button)
        {
            // Simulate click on the button
            SearchResult_Click(button, new RoutedEventArgs());
        }
    }

    private void OpenWebSearch(string query)
    {
        try
        {
            var searchUrl = _settingsService.Settings.WebSearchUrl;
            var encodedQuery = Uri.EscapeDataString(query);
            var fullUrl = searchUrl + encodedQuery;

            HideMenu();

            var startInfo = new ProcessStartInfo
            {
                FileName = fullUrl,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open web search: {ex.Message}");
        }
    }

    private Button CreateSearchResultButton(SearchResult result)
    {
        // For calculation results, use emoji icon; for others, get real icon from IconService
        var icon = result.Type == SearchResultType.Calculation ? null : _iconService.GetIcon(result.Path);

        var iconElement = icon != null
            ? (UIElement)new Image 
            { 
                Source = icon, 
                Width = 32, 
                Height = 32, 
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            }
            : (UIElement)new TextBlock 
            { 
                Text = GetFallbackIcon(result.Type), 
                FontSize = 24, 
                Margin = new Thickness(0, 0, 12, 0), 
                VerticalAlignment = VerticalAlignment.Center 
            };

        // For calculation results, show "Click to copy" hint
        var pathText = result.Type == SearchResultType.Calculation 
            ? "Sonucu panoya kopyalamak için tıklayın" 
            : result.Path;

        var button = new Button
        {
            Style = (Style)FindResource("SearchResultStyle"),
            Tag = result.Type == SearchResultType.Calculation ? result : result.Path,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    iconElement,
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = result.Name, FontSize = 14, Foreground = (Brush)FindResource("TextBrush") },
                            new TextBlock { Text = pathText, FontSize = 11, Foreground = (Brush)FindResource("TextSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 450 }
                        }
                    }
                }
            }
        };

        button.Click += SearchResult_Click;
        
        // Add right-click handler for context menu (only for non-calculation results)
        if (result.Type != SearchResultType.Calculation)
        {
            button.MouseRightButtonUp += SearchResult_RightClick;
        }
        
        return button;
    }

    private void SearchResult_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            ShowSearchResultContextMenu(path, button);
            e.Handled = true;
        }
    }

    private void ShowSearchResultContextMenu(string path, UIElement target)
    {
        var contextMenu = new ContextMenu();

        // "Aç" (Open) menu item
        var openItem = new MenuItem { Header = "Aç" };
        openItem.Click += (s, args) => LaunchItem(path);
        contextMenu.Items.Add(openItem);

        // "Dosya konumunu aç" (Open file location) menu item
        var openLocationItem = new MenuItem { Header = "Dosya konumunu aç" };
        openLocationItem.Click += (s, args) => OpenFileLocation(path);
        contextMenu.Items.Add(openLocationItem);

        contextMenu.Items.Add(new Separator());

        // Check if item is already pinned
        var isPinned = _pinnedItemsService.IsPinned(path);

        if (isPinned)
        {
            // "Kaldır" (Remove/Unpin) menu item
            var unpinItem = new MenuItem { Header = "Kaldır" };
            unpinItem.Click += (s, args) =>
            {
                _pinnedItemsService.RemovePin(path);
            };
            contextMenu.Items.Add(unpinItem);
        }
        else
        {
            // "Pinle" (Pin) menu item
            var pinItem = new MenuItem { Header = "Pinle" };
            pinItem.Click += (s, args) =>
            {
                var gridColumns = CalculateGridColumns();
                _pinnedItemsService.AddPin(path, _currentTabId, null, gridColumns);
            };
            contextMenu.Items.Add(pinItem);
        }

        contextMenu.PlacementTarget = target;
        contextMenu.IsOpen = true;
    }

    private static string GetFallbackIcon(SearchResultType type)
    {
        return type switch
        {
            SearchResultType.Application => "📦",
            SearchResultType.Folder => "📁",
            SearchResultType.File => "📄",
            SearchResultType.Calculation => "🔢",
            _ => "📄"
        };
    }

    private void SearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            // Check if this is a calculation result
            if (button.Tag is SearchResult result && result.Type == SearchResultType.Calculation)
            {
                // Copy the result to clipboard
                try
                {
                    Clipboard.SetText(result.Path); // Path contains the formatted result
                    
                    // Show brief feedback by changing button content temporarily
                    var originalContent = button.Content;
                    if (button.Content is StackPanel panel && panel.Children.Count > 1 && 
                        panel.Children[1] is StackPanel textPanel && textPanel.Children.Count > 0 &&
                        textPanel.Children[0] is TextBlock nameBlock)
                    {
                        var originalText = nameBlock.Text;
                        nameBlock.Text = "✓ Panoya kopyalandı!";
                        
                        // Restore after a short delay
                        Task.Delay(1000).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (nameBlock != null)
                                    nameBlock.Text = originalText;
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
                }
                return;
            }
            
            // Regular search result - launch the item
            if (button.Tag is string path)
            {
                LaunchItem(path);
            }
        }
    }
}
