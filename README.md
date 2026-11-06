# Playlist Maker

Windows desktop app that does one thing: build playlists. Browse your drives in a tree, drag music into a list, repair entries that point nowhere any more, save a portable M3U/M3U8 that still works after you move the files.

Built with WPF on .NET 8. No external packages, no telemetry, no installer - one executable.

## Features

**Explorer (left)**
- Lists every ready drive; folders expand lazily, one level at a time.
- Filtered by default to music and playlist files. Tick *Show all files* to see everything.
- Multi-select with Ctrl/Shift, drag the selection into the playlist, or use the right-click menu.
- **Alt+click a folder arrow** collapses that folder *and* every folder below it. Plain click collapses one level (recursive expand is deliberately not offered).
- Keyboard: **Right** expands the selected folder, **Left** collapses it, **Alt+Left** collapses it recursively, and **Left on a folder that is already collapsed selects the folder that contains it**. Up/Down/Home/End move the selection as usual.
- Expanding and collapsing never disturbs the selection - the rows you had selected stay selected, including with Ctrl/Shift multi-select.
- **Enter** adds the selection to the open playlist: music files and playlist files go straight in (a playlist file expands into its entries), and folders ask first whether to take only the files directly inside or all subfolders.
- **Drag rows onto a folder row to move the real files and folders** - hold **Ctrl** to copy instead. The target folder lights up while you hover it and the status line says what a drop will do. Dropping onto a file row means "into the folder that holds it"; dropping on empty space does nothing. A folder cannot be dropped into itself or into its own subfolder.
- **Drag rows out to Windows Explorer** to copy them there (Shift-drag moves them), and **drag files or folders from Windows Explorer into the tree** to move or copy them into the folder you drop them on.
- Name clashes are handled on the spot: *replace what is there*, *keep both* (the incoming item gets a `(2)` suffix) or *cancel* before anything is touched.
- After a move the tree follows the change: the destination opens, a moved folder keeps its expanded branch, the moved rows are selected again (so the next keystroke acts on them), and every playlist entry that pointed at a moved file or into a moved folder is re-pointed - `Ctrl+Z` puts the old paths back.

**Playlist (right)**
- **Tabs**, one playlist per tab. The `+` button adds a tab, double-clicking a tab label renames it (Enter commits, Escape cancels, and clicking anywhere outside the name box also commits), the small **x** closes it, and middle-clicking the tab does the same. Dropping files onto a tab header adds them to *that* tab's playlist - same rules as dropping onto the list.
- The tab name is the default file name when saving; the save dialog can of course change it.
- Closing a tab that still has unsaved work asks whether to save first, and the app asks per tab before it exits.
- Each tab has its own undo history and its own dirty marker (a `*` after the name).
- **Columns**: index, track name, path, album, artist, length and file size. Drag a header to reorder the columns, drag the header edge to resize, and **right-click any header** for a checklist that hides and shows each column (one column always stays visible, and *Show all columns* brings them back). Order, widths and visibility are remembered between launches in `%APPDATA%\PlaylistMaker\settings.json`.
- **Sort...** orders the current playlist by track number (from tags), file name, track name, file size, track length, album name or artist name - each ascending or descending - or shuffles it. Sorting is a normal edit, so `Ctrl+Z` restores the previous order. Ties fall back to the full path so the result is stable, and entries with an unknown length always sort to the end.
- Reorderable list: drag entries around, or move them with the keyboard. Deleting keeps the selection at the same index (clamped to the new end), so repeated `Delete` presses work down a list without re-clicking.
- Accepts drops from the tree, from Windows Explorer, and from the playlist itself.
- Dropping a playlist file pulls in its entries instead of the file.
- Broken entries stay in the list, marked with a yellow **!** and the tooltip *"File not found. Click here to fix."* Clicking it opens a picker where you choose the file itself or a folder to search by name; after one repair you are offered to fix the remaining broken entries with the same folder, optionally recursively.

**Context menu on the tree**
- Music file: *Add to playlist*.
- Folder: *Add to playlist* and *Save as playlist...* - both ask whether to take only the files directly inside or all subfolders.
- Multiple selected music files: *Add to playlist* and *Save as playlist...*.
- **Multiple selected folders: *Save each as playlist...*** - walks the folders one by one and treats each exactly like *Save as playlist*: the folder name is the default file name, the file is written next to the music, and a small dialog per folder offers **Save**, **Skip this folder** and **Cancel all** (Cancel all stops the whole run instead of just the current folder).
- Playlist file: *Add to playlist*.

*Save as playlist* additionally asks for the order: ID3 tag track number, file name A-Z, file name Z-A, track name A-Z, track name Z-A, or random. *Save each as playlist* asks that once and applies the same order to every folder.

When you save a single selected folder, the default playlist name is **that folder's name** (sanitised for Windows).

## Keyboard shortcuts

| Keys | Action |
| --- | --- |
| `Ctrl+S` | Save the playlist (always asks where) |
| `Ctrl+Up` / `Ctrl+Down` | Move selected entries up / down (works with multi-selection) |
| `Delete` | Remove selected entries (no confirmation) |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Next / previous playlist tab (wraps around) |
| `Ctrl+F4` | Close the open playlist tab (asks to save if it has unsaved work) |
| `Enter` in the tree | Add the selection to the open playlist |
| `E` | Show the selected entry in Windows Explorer |
| `Alt+click` on a folder arrow | Collapse the folder and everything under it |
| `Right` / `Left` in the tree | Expand / collapse the selected folder |
| `Alt+Left` in the tree | Collapse the selected folder recursively |

The save dialog starts in the deepest folder that contains every track of the playlist, and writes **relative paths** (forward slashes) whenever the playlist sits on the same drive - so the folder and the playlist can be moved or copied together and keep working.

## What it reads and writes

- Playlists: `.m3u`, `.m3u8` (read and write), `.pls` (read only).
- Music: mp3, mp2, flac, m4a, m4b, aac, ogg, oga, opus, wav, wma, aiff, aif, ape, wv, alac, dsf, dff, mpc, spx, tta, mid, midi, mod, xm, it, s3m, vgm, vgz, nsf, spc, psf, gbs.
- Title, track number, album and artist are read from ID3v2 tags (MP3) and Vorbis comments (FLAC/OGG). Other formats fall back to the file name, e.g. `03 - Intro.flac` becomes track 3, "Intro".
- Track lengths are read straight out of the container headers - no decoding and no external library: MP3 (Xing/Info frame count, otherwise a constant-bitrate estimate), FLAC (STREAMINFO), WAV (RIFF fmt/data), OGG/Opus (last granule position) and M4A/MP4 (mvhd box). Formats outside that list show a dash instead of a length. Metadata, lengths and sizes are read lazily per row, so a 50 000 track playlist only pays for what is on screen.

## Build

Requires the .NET 8 SDK on Windows:

```
dotnet build src/PlaylistMaker/PlaylistMaker.csproj -c Release
dotnet run --project src/PlaylistMaker/PlaylistMaker.csproj
```

CI (GitHub Actions, `windows-latest`) builds and publishes a framework-dependent `win-x64` zip as the `PlaylistMaker-win-x64` artifact - that needs the .NET 8 Desktop Runtime, not the full SDK.

Regenerate the icon with `python tools/generate-icon.py` (needs Pillow).

## Licence

MIT.
