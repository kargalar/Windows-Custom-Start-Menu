# Implementation Plan: Start Menu Enhancements

## Overview

Bu plan, Custom Start Menu uygulamasına 15 yeni özellik eklemek için adım adım implementasyon görevlerini tanımlar. Görevler mantıksal sırayla organize edilmiş olup, her biri önceki görevlerin üzerine inşa edilmektedir.

## Tasks

- [x] 1. Create SettingsService and AppSettings model
  - Create `Models/AppSettings.cs` with properties: ShowIconsOnly, MenuTransparency, PinnedItemsLayout, WebSearchUrl
  - Create `Services/SettingsService.cs` with Load, Save, UpdateSetting methods
  - Store settings in `%LocalAppData%/CustomStartMenu/settings.json`
  - Add SettingsChanged event for UI updates
  - _Requirements: 1.1, 1.3, 1.4_

- [ ]* 1.1 Write property test for settings round-trip persistence
  - **Property 1: Settings Round-Trip Persistence**
  - **Validates: Requirements 1.3, 1.4**

- [x] 2. Add Settings Panel UI
  - [x] 2.1 Create Settings button in footer area
    - Add settings gear icon button next to power button
    - _Requirements: 1.1_

  - [x] 2.2 Create Settings page view (in-window navigation)
    - Create settings page as a Grid/Panel that replaces main content
    - Add back button to return to main view
    - Add "Show icons only" toggle switch
    - Add transparency slider (0-100%)
    - Add layout mode switch (Sıralı/Serbest)
    - Wire up settings to SettingsService
    - Implement page navigation (show/hide content areas)
    - _Requirements: 1.2, 1.3, 1.4, 2.1, 7.1, 15.1_

- [x] 3. Implement Icon-Only Display Mode
  - Modify `CreatePinnedItemButton` to check ShowIconsOnly setting
  - Hide text labels when enabled
  - Reduce button size to 60x60 when enabled
  - Subscribe to SettingsChanged to refresh UI
  - _Requirements: 2.2, 2.3, 2.4_

- [ ]* 3.1 Write property test for icon-only mode display behavior
  - **Property 2: Icon-Only Mode Display Behavior**
  - **Validates: Requirements 2.2, 2.3, 2.4**

- [x] 4. Implement Menu Transparency
  - Add transparency binding to MainBorder Background
  - Update opacity when transparency setting changes
  - Apply saved transparency on window load
  - _Requirements: 7.2, 7.3, 7.4_

- [ ]* 4.1 Write property test for transparency setting application
  - **Property 6: Transparency Setting Application**
  - **Validates: Requirements 7.2, 7.3**

- [x] 5. Remove Title Text
  - Remove "Custom Start Menu" TextBlock from XAML
  - Adjust layout to use freed space for tabs
  - _Requirements: 9.1, 9.2_

- [ ] 6. Checkpoint - Settings and UI basics
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Enhance SearchService for Application Discovery
  - [x] 7.1 Add Registry application search
    - Search `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
    - Search `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
    - Extract DisplayName and InstallLocation
    - _Requirements: 3.1_

  - [x] 7.2 Add AppData application search
    - Search `%LocalAppData%\Programs`
    - Search `%AppData%\Microsoft\Windows\Start Menu\Programs`
    - Find .exe files and shortcuts
    - _Requirements: 3.2_

  - [x] 7.3 Add UWP/Store application search
    - Use Windows.Management.Deployment.PackageManager
    - Get installed packages with DisplayName
    - Create launch URIs for UWP apps
    - _Requirements: 3.3_

  - [x] 7.4 Combine all search sources
    - Merge results from all sources
    - Remove duplicates by path
    - Update caching to include all sources
    - _Requirements: 3.4, 3.5_

- [ ]* 7.5 Write property test for search result completeness
  - **Property 3: Search Result Completeness**
  - **Validates: Requirements 3.4**

- [ ]* 7.6 Write property test for search caching behavior
  - **Property 4: Search Caching Behavior**
  - **Validates: Requirements 3.5**

- [x] 8. Implement MathEvaluator
  - Create `Services/MathEvaluator.cs`
  - Implement expression parsing with DataTable.Compute or custom parser
  - Support +, -, *, /, parentheses, decimals
  - Add TryEvaluate method returning bool with out result
  - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [ ]* 8.1 Write property test for math expression evaluation
  - **Property 5: Math Expression Evaluation**
  - **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

- [x] 9. Integrate Math Results in Search
  - Check if query is math expression before searching
  - Display calculation result as first search result
  - Add click handler to copy result to clipboard
  - _Requirements: 6.5, 6.6_

- [ ] 10. Checkpoint - Search enhancements
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Implement Keyboard Navigation in Search
  - [x] 11.1 Add selected index tracking
    - Add `_selectedSearchIndex` field
    - Initialize to -1 (no selection)
    - _Requirements: 4.1_

  - [x] 11.2 Handle arrow key navigation
    - Up arrow: decrement index (or trigger web search if at -1)
    - Down arrow: increment index
    - Wrap around at boundaries
    - _Requirements: 4.1, 4.4_

  - [x] 11.3 Add visual selection highlight
    - Create selected style for search results
    - Apply/remove style based on selected index
    - _Requirements: 4.2_

  - [x] 11.4 Handle Enter key to launch
    - Launch selected item on Enter
    - If no selection, launch first result
    - _Requirements: 4.3_

- [x] 12. Implement Web Search Integration
  - Add web search trigger on Up arrow when index is -1
  - Use WebSearchUrl from settings
  - Open default browser with search query
  - _Requirements: 5.1, 5.2, 5.3_

- [ ]* 12.1 Write unit test for web search URL construction
  - Test that query is correctly URL-encoded
  - Test configurable search engine URL
  - _Requirements: 5.2, 5.3_

- [x] 13. Add Search Result Context Menu
  - [x] 13.1 Add right-click handler to search results
    - Create MouseRightButtonUp handler
    - Build context menu dynamically
    - _Requirements: 11.1_

  - [x] 13.2 Add Pin/Unpin menu item
    - Check IsPinned status
    - Show "Pinle" or "Kaldır" accordingly
    - Wire up click handlers
    - _Requirements: 11.2, 11.3_

  - [x] 13.3 Add Open and Open Location menu items
    - Add "Aç" menu item
    - Add "Dosya konumunu aç" menu item
    - _Requirements: 11.4, 11.5_

- [ ]* 13.4 Write property test for context menu pin state correctness
  - **Property 9: Context Menu Pin State Correctness**
  - **Validates: Requirements 11.2, 11.3**

- [ ] 14. Checkpoint - Search and navigation
  - Ensure all tests pass, ask the user if questions arise.

- [x] 15. Add Custom Name Support for Pinned Items
  - [x] 15.1 Update PinnedItem model
    - Add CustomName property
    - Add DisplayName computed property
    - _Requirements: 12.3_

  - [x] 15.2 Add Rename option to context menu
    - Add "Yeniden Adlandır" menu item
    - Show rename dialog on click
    - _Requirements: 12.1, 12.2_

  - [x] 15.3 Update display to use DisplayName
    - Modify CreatePinnedItemButton to use DisplayName
    - _Requirements: 12.3_

- [ ]* 15.4 Write property test for custom name storage integrity
  - **Property 10: Custom Name Storage Integrity**
  - **Validates: Requirements 12.3, 12.4**

- [x] 16. Add Internet Shortcut Support
  - [x] 16.1 Update PinnedItemType enum
    - Add InternetShortcut type
    - _Requirements: 10.1_

  - [x] 16.2 Update AddPin to handle .url files
    - Detect .url extension
    - Set type to InternetShortcut
    - _Requirements: 10.1_

  - [x] 16.3 Update icon display for .url files
    - Extract icon from .url file or use default browser icon
    - _Requirements: 10.2_

  - [x] 16.4 Update launch to handle .url files
    - Use ShellExecute to open in default browser
    - _Requirements: 10.3_

- [ ]* 16.5 Write property test for URL file pinning support
  - **Property 8: URL File Pinning Support**
  - **Validates: Requirements 10.1**

- [x] 17. Improve Drag-to-Group Functionality
  - [x] 17.1 Enhance group folder drop target
    - Improve visual feedback on drag over
    - Scale up and highlight on DragEnter
    - _Requirements: 13.1_

  - [x] 17.2 Fix item-to-group drop handling
    - Ensure MoveItemToGroup is called correctly
    - Refresh UI after drop
    - _Requirements: 13.2_

- [ ]* 17.3 Write property test for drag-to-group moves item
  - **Property 11: Drag-to-Group Moves Item**
  - **Validates: Requirements 13.2**

- [x] 18. Implement Group Drag-to-Reorder
  - [x] 18.1 Add drag support to group folders
    - Add PreviewMouseLeftButtonDown handler
    - Implement drag initiation
    - _Requirements: 14.1_

  - [x] 18.2 Add drop handling for group reorder
    - Calculate drop position
    - Call MoveGroup method
    - _Requirements: 14.1_

  - [x] 18.3 Add MoveGroup method to PinnedItemsService
    - Implement group reordering logic
    - Persist new order
    - _Requirements: 14.3_

- [ ]* 18.4 Write property test for group order persistence
  - **Property 12: Group Order Persistence**
  - **Validates: Requirements 14.3**

- [ ] 19. Checkpoint - Pinning and drag-drop
  - Ensure all tests pass, ask the user if questions arise.

- [x] 20. Implement Layout Mode (Ordered vs Free-Form)
  - [x] 20.1 Update PinnedItem model for grid positions
    - Add GridRow and GridColumn nullable properties
    - _Requirements: 15.3, 15.4_

  - [x] 20.2 Implement Ordered layout mode
    - Auto-arrange items in WrapPanel
    - Clear grid positions when in Ordered mode
    - _Requirements: 15.2_

  - [x] 20.3 Implement Free-Form layout mode
    - Replace WrapPanel with Grid when in FreeForm mode
    - Allow placing items at specific grid positions
    - _Requirements: 15.3_

  - [x] 20.4 Implement mode switching
    - Handle transition from FreeForm to Ordered
    - Rearrange items automatically
    - _Requirements: 15.5_

- [ ]* 20.5 Write property test for layout mode behavior
  - **Property 13: Layout Mode Behavior**
  - **Validates: Requirements 15.2, 15.3, 15.4, 15.5**

- [x] 21. Update Context Menu Service for Pin/Unpin
  - [x] 21.1 Create helper app for context menu actions
    - Handle --pin and --unpin command line arguments
    - Check if item is already pinned
    - _Requirements: 8.1, 8.2_

  - [x] 21.2 Update registry entries
    - Register dynamic context menu handler
    - Or use separate registry keys for pin/unpin
    - _Requirements: 8.1, 8.2, 8.3_

- [ ]* 21.3 Write property test for unpin action removes item
  - **Property 7: Unpin Action Removes Item**
  - **Validates: Requirements 8.3**

- [ ] 22. Final Checkpoint
  - Ensure all tests pass, ask the user if questions arise.
  - Verify all 15 requirements are implemented
  - Test end-to-end user flows

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties
- Unit tests validate specific examples and edge cases
- Implementation uses C# with WPF and FsCheck for property-based testing
