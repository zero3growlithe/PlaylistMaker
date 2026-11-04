# Notes for agents working on this repository

## Build and verification
- There is **no .NET SDK on the development host** used by the agent. CI is the compiler: push to `main`, then read the `Build` workflow run and its `dotnet build` output. Never claim a successful build without a green run.
- Do not add NuGet packages. The app is deliberately dependency-free so the CI stays trivial and the output stays a single small exe. Anything you would reach for (tag libraries, drag-drop helpers, MVVM frameworks) is implemented in `Services/` and `ViewModels/` instead.
- `ImplicitUsings` is disabled: every file needs its own `using` directives. A forgotten one surfaces only as `CS0246` from CI - check the `using` block when adding a file.

## Layout
```
src/PlaylistMaker/
  MainWindow.xaml(.cs)        window layout + all UI event wiring (code-behind)
  ViewModels/MainViewModel.cs state: flattened tree, playlist, undo/redo
  Models/                     FileSystemNode, PlaylistEntry, PlaylistSortMode, TrackMetadata
  Services/                   file system scanning, playlist IO, tag reading, ordering,
                              missing-file repair, path helpers, undo stack
  Views/                      three small dialogs: scope, ordering, missing file
```

## Design decisions worth keeping
- **The tree is flattened on purpose.** `TreeView` cannot multi-select, and multi-select is a hard requirement of this app. `MainViewModel.VisibleNodes` is rebuilt from the node tree each time something expands or collapses; `FileSystemNode.Level` drives the indentation (`IndentMargin`), and the chevron is a `Button` in the item template.
- **Undo is snapshot based.** Every mutation calls `MainViewModel.RecordUndo()` *before* touching `PlaylistEntries`; undo/redo then rebuilds the entries from a path list. This keeps correctness cheap - do not introduce per-operation inverse logic.
- **"Save each as playlist" is a loop, not a batch write.** One `SaveEachPlaylistWindow` per folder returns `SaveEachAction.Save / Skip / CancelAll`; `CancelAll` (and closing the window with the X) aborts the remaining folders, while `Skip` only moves on. The recursive/ordering questions are asked **once** before the loop - asking them per folder would be four dialogs per folder.
- **The tab-strip `+` needs `TabAddButton`, not the implicit Button style.** The implicit style carries `Padding="10,4"` and `MinWidth="72"`, which silently clip a one-character glyph inside a fixed-width box. Any single-glyph button needs the same treatment (`TabCloseButton`, `ChevronButton`, `IconButton` all set `Padding="0"`).
- **Window-level shortcuts live in `OnWindowPreviewKeyDown`**, which bails out unless Ctrl is held - so plain-key gestures (Enter, arrows, E, Delete) must be handled on the control itself. `Ctrl+Tab` is intercepted there *before* focus navigation gets a chance to move it, and `SwitchTab` wraps with `((current + offset) % count + count) % count` and scrolls the header into view.
- **Enter and the "Add to playlist" menu item share `AddSelectionToPlaylist()`** so the tree keyboard path and the context menu can never drift apart.
- **Rebuilding `VisibleNodes` destroys the tree selection.** `RebuildVisibleNodes` clears and refills the collection, so the ListBox drops every selected row. `ToggleExpansion` snapshots the selected paths first and re-selects the surviving rows afterwards - always go through it instead of calling `viewModel.ToggleExpand` directly.
- **Parent lookup must normalise trailing separators.** Drive roots are created from `DriveInfo.Name` (`C:\`, with a backslash) while `DirectoryInfo.FullName` children never end in one, so `SelectParentFolder` trims both sides before comparing.
- **Rename commits on window-level `PreviewMouseDown`**, not just `TextBox.LostFocus`: clicking a non-focusable surface never moves focus, so LostFocus alone leaves the editor stuck open. The handler ignores clicks inside the editor and leaves the click unhandled so it still selects whatever it hit.
- **`FindAncestor` falls back to `LogicalTreeHelper` for non-visuals.** A click on text can originate from a `Run`, and `VisualTreeHelper.GetParent` throws for those - inside a `PreviewMouseDown` that would be an unhandled crash.
- **Arrow keys on the tree are handled in `PreviewKeyDown`** so the ListBox does not consume Left/Right first; only folders are affected, files fall through to default selection movement.
- **`RemoveSelectedEntries` restores the selection by index** (first deleted index, clamped to the new count) *after* `BindActiveTab()`, because re-pointing `ItemsSource` clears the selection.
- **Hidden columns keep their slot.** `PlaylistColumnSet` hides a column by setting `Width = 0` instead of removing it, because `GridView.Columns` is the only authority for display order (drag-reordering mutates it and raises `CollectionChanged`). Removing columns would lose track of where a hidden column belongs. `SaveInto` reads the order out of `GridView.Columns`, so never reorder `columnSet.Columns` without also moving the GridView columns.
- **Column layout is written on `GridView.Columns.CollectionChanged`** (drag reorder), on every show/hide, and on window close (widths raise no event). `PlaylistColumnSet.IsApplyingLayout` suppresses the save that the initial `Move` calls would otherwise trigger.
- **`PlaylistEntry.Index` is owned by `PlaylistTab`**, which renumbers on every collection change; the setter ignores no-op writes so a reorder costs one notification per actually-moved row.
- **Row metadata is lazy and cached.** `TrackName`/`Album`/`Artist` come from `TagReader` (cached by path), lengths from `MediaDurationReader` (cached), sizes from `FileInfo`. Do not call these in a tight loop over every entry expecting them to be free - sorting by length/album does touch every row once, which is why the grid is virtualized and the readers cache.
- **`MediaDurationReader` bit layouts were verified against ffprobe** for mp3 (CBR and VBR), flac, wav, ogg, opus and m4a. If you touch a parser, re-verify with real files - a wrong offset silently yields a plausible but wrong duration.
- **Sorting goes through `MainViewModel.SortActiveTab`**, which takes an undo snapshot first; `PlaylistSorter` returns a new ordered list and never mutates the tab.
- **Every playlist is a `PlaylistTab`** (`ViewModels/PlaylistTab.cs`) owning its own `Entries`, `UndoStack`, `FilePath` and dirty flag. `MainViewModel.ActiveTab` is what every mutation acts on, so never reach for a global entries collection - there isn't one any more. `MainWindow.BindActiveTab()` re-points `PlaylistList.ItemsSource` and re-syncs `TabStrip.SelectedItem` (guarded by `isSyncingTabSelection` to avoid selection recursion).
- **Tab headers are a horizontal `ListBox`** with a custom `ListBoxItem` template (tab shape), not a `TabControl`: a `TabControl` cannot host per-header drop targets and a close button cleanly, and its content presenter would fight the single shared `PlaylistList`. Tab drops resolve the target through `InputHitTest` -> `ListBoxItem` -> `PlaylistTab`.
- **Inline rename** toggles `PlaylistTab.IsRenaming`, which swaps a `TextBlock` for a `TextBox` through two `Visibility` properties; `LostFocus` commits, and `CommitRename` is idempotent because focus juggling can fire it twice.
- **Unsaved-work prompts** hang off `PlaylistTab.NeedsSavePrompt` (entries present AND either dirty or never saved) and are used both by tab close and by `Window.Closing`.
- **Playlist entries are objects, not strings**, because a broken entry has to keep its place and be repairable in situ (`PlaylistEntry.PointTo`).
- **Relative paths on save.** `PlaylistFileService.BuildEntry` uses `Path.GetRelativePath` and forward slashes when the playlist shares a drive with the music, otherwise an absolute path. Never write absolute paths unconditionally.
- **No external dialogs beyond the BCL.** Folder picking uses `Microsoft.Win32.OpenFolderDialog` (available in .NET 8 WPF). Do not pull in WinForms for `FolderBrowserDialog`.
- **Tag reading is intentionally narrow**: ID3v2.3/2.4 frames `TIT2`/`TRCK`, and FLAC Vorbis comments `TITLE`/`TRACKNUMBER`. Everything else falls back to the file name via `TagReader.FromFileName`. Extend `TagReader` rather than adding a package.

## Pitfalls hit before
- `x:Class` in `Views/*.xaml` must stay `PlaylistMaker.Views.*`; the folder name is part of the namespace.
- `StaticResource` keys used in dialogs are declared in `App.xaml` - add new keys there, not per window.
- `Visibility` bindings are typed (`Visibility`, not `string`) on `FileSystemNode`/`PlaylistEntry`: a string would rely on an implicit enum conversion.
- Drag payloads carry both a private format (`PlaylistMaker.PlaylistEntry`, for internal reorder) and `FileDrop` (so dragging out to Explorer copies the files). The drop handler must check the private format first.
- `Window.PreviewKeyDown` tunnels from the window down, so window-level `Ctrl+S/Z/Y` runs before the playlist list's own key handler - keep `Delete`, `E` and `Ctrl+Up/Down` in the list handler only, so they do not fire while the tree has focus.
