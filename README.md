# VenToMe

A tiny Windows systray app. Press **Ctrl+Alt+G** (or left-click the tray icon) and every
window on every display is moved onto the display your mouse cursor is on.

- Windows keep their size (shrunk only if larger than the target screen) and their relative position.
- Minimized and maximized windows are moved too and keep their state.
- Right-click the tray icon for "Start with Windows" and "Exit".
- Windows belonging to elevated (admin) programs can't be moved unless VenToMe is also run as admin.

## Build

Requires the .NET 8 SDK on Windows:

    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

The exe lands in `bin/Release/net8.0-windows/win-x64/publish/`.

To change the hotkey, edit `Modifiers` / `Key` at the top of `TrayContext` in `Program.cs`.
