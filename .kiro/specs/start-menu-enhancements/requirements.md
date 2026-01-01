# Requirements Document

## Introduction

Bu belge, Custom Start Menu uygulaması için kapsamlı geliştirme gereksinimlerini tanımlar. Özellikler arasında ayarlar menüsü, gelişmiş arama, klavye navigasyonu, matematik hesaplama, saydamlık ayarları, gelişmiş pinleme özellikleri ve sürükle-bırak iyileştirmeleri bulunmaktadır.

## Glossary

- **Start_Menu**: Ana uygulama penceresi
- **Settings_Panel**: Kullanıcı tercihlerini yönetmek için ayarlar arayüzü
- **Search_Service**: Dosya, uygulama ve hesaplama araması yapan servis
- **Pinned_Item**: Kullanıcının hızlı erişim için sabitlediği dosya, klasör veya uygulama
- **Context_Menu**: Sağ tık ile açılan menü
- **Search_Result**: Arama sonucu öğesi
- **Group**: Pinlenmiş öğelerin organize edildiği klasör benzeri yapı
- **Layout_Mode**: Pinlenmiş öğelerin düzeni (sıralı veya serbest)
- **Transparency**: Menü arka planının saydamlık seviyesi

## Requirements

### Requirement 1: Settings Panel

**User Story:** As a user, I want to access a settings panel, so that I can customize the application behavior and appearance.

#### Acceptance Criteria

1. THE Start_Menu SHALL provide a settings button in the footer area
2. WHEN the user clicks the settings button, THE Start_Menu SHALL navigate to a settings page within the same window (not a separate dialog)
3. THE Settings_Panel SHALL display as a full-page view replacing the current content
4. THE Settings_Panel SHALL provide a back button to return to the previous view
5. THE Settings_Panel SHALL persist all settings to local storage immediately upon change
6. WHEN the application starts, THE Start_Menu SHALL load and apply saved settings

### Requirement 2: Icon-Only Display Mode

**User Story:** As a user, I want to show only icons for pinned items, so that I can fit more items on screen.

#### Acceptance Criteria

1. THE Settings_Panel SHALL provide a toggle switch for "Show icons only" option
2. WHEN "Show icons only" is enabled, THE Start_Menu SHALL display pinned items without text labels
3. WHEN "Show icons only" is enabled, THE Pinned_Item buttons SHALL be smaller (60x60 instead of 100x100)
4. WHEN "Show icons only" is disabled, THE Start_Menu SHALL display pinned items with text labels

### Requirement 3: Enhanced Application Search

**User Story:** As a user, I want to search for installed applications like Spotify, so that I can quickly find and launch any app.

#### Acceptance Criteria

1. THE Search_Service SHALL search Windows Registry for installed applications
2. THE Search_Service SHALL search AppData folders for user-installed applications
3. THE Search_Service SHALL search Microsoft Store apps (UWP applications)
4. WHEN searching, THE Search_Service SHALL return results from all application sources
5. THE Search_Service SHALL cache application index for performance

### Requirement 4: Keyboard Navigation in Search Results

**User Story:** As a user, I want to navigate search results with arrow keys, so that I can quickly select items without using the mouse.

#### Acceptance Criteria

1. WHEN search results are displayed, THE Start_Menu SHALL allow navigation with Up/Down arrow keys
2. THE Start_Menu SHALL visually highlight the currently selected search result
3. WHEN the user presses Enter on a selected result, THE Start_Menu SHALL launch that item
4. WHEN the user presses Up arrow as the first action after typing, THE Start_Menu SHALL open a web browser search with the query

### Requirement 5: Web Search Integration

**User Story:** As a user, I want to search the web directly from the start menu, so that I can quickly look up information.

#### Acceptance Criteria

1. WHEN the user presses Up arrow before selecting any result, THE Start_Menu SHALL open the default browser with a web search
2. THE Start_Menu SHALL use the current search query as the web search term
3. THE Start_Menu SHALL use a configurable search engine URL (default: Google)

### Requirement 6: Math Expression Calculator

**User Story:** As a user, I want to see calculation results when I type math expressions, so that I can quickly perform calculations.

#### Acceptance Criteria

1. WHEN the search query is a valid math expression, THE Search_Service SHALL evaluate and display the result
2. THE Search_Service SHALL support basic operations: addition (+), subtraction (-), multiplication (*), division (/)
3. THE Search_Service SHALL support parentheses for grouping
4. THE Search_Service SHALL support decimal numbers
5. THE Search_Service SHALL display the calculation result as the first search result
6. WHEN the user clicks the calculation result, THE Start_Menu SHALL copy the result to clipboard

### Requirement 7: Menu Transparency Settings

**User Story:** As a user, I want to adjust the menu transparency, so that I can customize the visual appearance.

#### Acceptance Criteria

1. THE Settings_Panel SHALL provide a slider for adjusting menu transparency (0-100%)
2. WHEN the transparency value changes, THE Start_Menu SHALL immediately update its background opacity
3. THE Start_Menu SHALL persist the transparency setting between sessions
4. THE Start_Menu SHALL have a default transparency of 85%

### Requirement 8: Context-Aware Pin/Unpin in Explorer

**User Story:** As a user, I want the Explorer context menu to show "Unpin" instead of "Pin" for already pinned items, so that I can easily manage pinned items.

#### Acceptance Criteria

1. WHEN a file is already pinned, THE Context_Menu in Explorer SHALL show "Kaldır" (Remove) option
2. WHEN a file is not pinned, THE Context_Menu in Explorer SHALL show "Pinle" (Pin) option
3. WHEN the user clicks "Kaldır", THE Start_Menu SHALL remove the item from pinned items

### Requirement 9: Remove Title Text

**User Story:** As a user, I want a cleaner interface without the "Custom Start Menu" title, so that I have more space for content.

#### Acceptance Criteria

1. THE Start_Menu SHALL NOT display the "Custom Start Menu" title text at the top
2. THE Start_Menu SHALL use the freed space for tabs or content

### Requirement 10: Internet Shortcut Pinning

**User Story:** As a user, I want to pin internet shortcuts (.url files), so that I can quickly access my favorite websites.

#### Acceptance Criteria

1. THE Start_Menu SHALL support pinning .url (internet shortcut) files
2. THE Start_Menu SHALL display the appropriate icon for internet shortcuts
3. WHEN launching an internet shortcut, THE Start_Menu SHALL open it in the default browser

### Requirement 11: Search Result Context Menu

**User Story:** As a user, I want to right-click search results to pin them or open their location, so that I can manage items directly from search.

#### Acceptance Criteria

1. WHEN the user right-clicks a Search_Result, THE Start_Menu SHALL display a context menu
2. THE Context_Menu SHALL include "Pinle" (Pin) option for unpinned items
3. THE Context_Menu SHALL include "Kaldır" (Remove) option for already pinned items
4. THE Context_Menu SHALL include "Dosya konumunu aç" (Open file location) option
5. THE Context_Menu SHALL include "Aç" (Open) option

### Requirement 12: Pinned Item Custom Names

**User Story:** As a user, I want to rename pinned items independently of the file name, so that I can use custom labels.

#### Acceptance Criteria

1. THE Context_Menu for Pinned_Item SHALL include "Yeniden Adlandır" (Rename) option
2. WHEN renaming, THE Start_Menu SHALL show a dialog to enter the new name
3. THE Start_Menu SHALL store the custom name separately from the file path
4. THE Start_Menu SHALL NOT modify the actual file name on disk

### Requirement 13: Drag Items to Groups

**User Story:** As a user, I want to drag pinned items into groups, so that I can organize my items easily.

#### Acceptance Criteria

1. WHEN dragging a Pinned_Item over a Group folder, THE Start_Menu SHALL highlight the group as a drop target
2. WHEN dropping a Pinned_Item on a Group folder, THE Start_Menu SHALL move the item into that group
3. THE Start_Menu SHALL provide visual feedback during drag operations

### Requirement 14: Drag to Reorder Groups

**User Story:** As a user, I want to drag groups to reorder them, so that I can organize my groups.

#### Acceptance Criteria

1. THE Start_Menu SHALL allow dragging Group folders to reorder them
2. WHEN dragging a Group, THE Start_Menu SHALL show visual feedback for the drop position
3. THE Start_Menu SHALL persist the new group order

### Requirement 15: Layout Mode Setting

**User Story:** As a user, I want to choose between ordered and free-form layout for pinned items, so that I can arrange items my way.

#### Acceptance Criteria

1. THE Settings_Panel SHALL provide a switch for "Sıralı" (Ordered) vs "Serbest" (Free-form) layout
2. WHEN "Sıralı" mode is active, THE Start_Menu SHALL automatically arrange items in a grid
3. WHEN "Serbest" mode is active, THE Start_Menu SHALL allow placing items at any grid position
4. WHEN in "Serbest" mode, THE Start_Menu SHALL persist item positions
5. WHEN switching from "Serbest" to "Sıralı", THE Start_Menu SHALL rearrange items automatically
