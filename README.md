# VenToMe

A tiny Windows systray app that pulls every window from every display onto the one you're working on.

Press **Ctrl+Win+G** (or left-click the tray icon) and all windows move to the display your mouse cursor is on.
Press **Ctrl+Win+Z** to send them back.

## Features

- **Gather:** windows keep their size (shrunk only if larger than the target display) and their relative position.
- **Undo:** only the windows that were moved go back to their original display, position, size and min/max state.
  Windows closed in the meantime are skipped. Gathering repeatedly before undoing still returns windows to where they started.
- Minimized and maximized windows are moved too and keep their state.
- **Configurable hotkeys:** right-click the tray icon and choose **Hotkeys...**, click a box and press the new
  combination (it must include Ctrl, Alt, Shift or Win). Changes apply immediately and are saved to
  `%AppData%\VenToMe\settings.json`.
- **Start with Windows** toggle in the tray menu.
- Only one instance runs at a time.

## Install

Download `VenToMe-win-x64.zip` from the [Releases](../../releases) page, extract it anywhere and run `VenToMe.exe`.
It is a single ~0.2 MB exe for 64-bit Windows and needs the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed.

There is no installer and nothing is written outside `%AppData%\VenToMe` (settings) and, if you enable it,
the per-user "Start with Windows" registry entry.

## Notes and limits

- Windows belonging to elevated (admin) programs can't be moved unless VenToMe is also run as admin.
- Windows reserves some Win-key shortcuts (Win+L, Win+D, Win+E, Win+G...). If a hotkey is taken, VenToMe shows a
  warning and the tray menu still works.
- Undo is one level deep and only lasts while the app is running.

## Build

Requires the .NET 8 SDK on Windows:

    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

The exe lands in `bin/Release/net8.0-windows/win-x64/publish/`.

To regenerate the icon, run `pwsh tools/make-icon.ps1`.

## Releasing

Pushing a tag that starts with `v` runs `.github/workflows/release.yml`, which builds the zip on GitHub and
attaches it to a new release with generated notes:

    git tag v1.0.0
    git push origin v1.0.0

The workflow can also be started manually from the Actions tab; that run only uploads the zip as a build artifact.
