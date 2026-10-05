# GUI scaling validation

This change keeps WinForms per-monitor DPI scaling and adds responsive layout in
ordinary source files. Designer files and the pinned SDUI submodule are unchanged.
The main window fits within the monitor working area, its footer wraps when
necessary, and the character summary places statistics below its bars on narrow
windows. Embedded pages keep their original layout size and scroll when the
available viewport is smaller. View > Pages provides access to crowded title tabs.
The sidebar temporarily hides below 950 logical pixels of window width; it returns
when there is enough space and the user's Sidebar preference is still enabled.

## Build on Windows

Use a separate checkout of branch `codex/gui-dpi`, or copy this worktree to Windows.
From its root, run:

```powershell
powershell.exe -ExecutionPolicy Bypass .\build.ps1 -DoNotStart
```

The build script terminates running RSBot, RSBot.Manager, and sro_client processes.
Run it when those processes can be stopped. Inspect `build.log` for errors, then
launch `Build\RSBot.exe` for the checks below.

## Display matrix

Test a fresh launch at each combination. Display scaling is changed in Windows
Settings > System > Display, not by changing the game's resolution.

| Display resolution | Scaling | DPI |
| --- | --- | --- |
| 1920 × 1080 | 100%, 125%, 175% | 96, 120, 168 |
| 2560 × 1440 | 100%, 125%, 175% | 96, 120, 168 |
| 2560 × 1600 | 100%, 125%, 175% | 96, 120, 168 |

For each combination:

1. Check that the title bar, footer, and status bar stay inside the usable desktop.
2. Resize between the minimum window size and maximized. Confirm the server
   selectors, IP Bind, Save, and Start/Stop do not overlap, and are keyboard accessible.
3. Check the character name/level, HP/MP/EXP, STR/INT, Gold, and SP. Statistics
   should move below the bars on narrow windows; long values should truncate cleanly.
4. Visit every plugin and botbase page. Scroll horizontally and vertically to reach
   the rightmost and bottommost controls; check inner tabs and their translated captions.
5. Scroll a page, switch pages, and return. Verify the new page starts at its top,
   and scrollbars disappear when that page fits. Switch botbases repeatedly after
   widening the window; a reused view must not acquire a larger minimum layout.
6. Check View > Pages selects the correct page even when its title caption is crowded.
7. Toggle View > Sidebar, resize across the sidebar threshold, and confirm the saved
   preference is respected when the window is widened again.
8. Move the open window between monitors at 100%, 125%, and 175%, then back. Check
   that text, controls, scrolling, and dropdown item heights rescale without growing
   progressively. Repeat with maximized and restored windows.

Compilation and interactive rendering require Windows. Linux source review and
`git diff --check` do not establish that all plugin layouts render correctly.
