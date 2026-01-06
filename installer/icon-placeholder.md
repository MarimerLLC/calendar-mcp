# Icon Placeholder

This directory should contain `calendar-mcp-icon.ico` for the Windows installer.

## Creating an Icon

1. Design or obtain a 256x256 PNG image representing Calendar MCP
2. Convert to ICO format using an online tool or software like:
   - [ConvertICO](https://convertico.com/)
   - GIMP
   - Photoshop

## Icon Requirements

- Format: .ico (Windows Icon)
- Recommended sizes: 16x16, 32x32, 48x48, 256x256
- Square aspect ratio
- Transparent background (optional)

## Temporary Solution

If you don't have an icon, comment out this line in `CalendarMcp-Setup.iss`:

```iss
; SetupIconFile=calendar-mcp-icon.ico
```

The installer will use the default Inno Setup icon.
