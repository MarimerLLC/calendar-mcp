# Calendar MCP Installer

This directory contains the Windows installer configuration for Calendar MCP.

## Files

- **CalendarMcp-Setup.iss**: Inno Setup script for creating the Windows installer
- **installer-readme.txt**: Information displayed to users during installation
- **calendar-mcp-icon.ico**: Application icon (optional, create if needed)

## Building the Installer

### Prerequisites

1. Install [Inno Setup](https://jrsoftware.org/isinfo.php) (free)
2. Build the Calendar MCP projects in Release mode
3. Ensure release binaries are in `../release/calendar-mcp-win-x64/`

### Build Process

#### Manual Build

```bash
# 1. Build the projects
dotnet publish src/CalendarMcp.Cli/CalendarMcp.Cli.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true

# 2. Run Inno Setup compiler
iscc installer/CalendarMcp-Setup.iss
```

The installer will be created as `release/calendar-mcp-setup-win-x64.exe`

#### Automated Build (GitHub Actions)

The GitHub Actions workflow (`.github/workflows/release.yml`) will automatically build the installer when:
- Changes are pushed to the `release` branch
- A version tag (e.g., `v1.0.0`) is created

The installer will be uploaded as a release asset.

## Installer Features

- **Installation Directory**: Default to `C:\Program Files\Calendar MCP\`
- **PATH Addition**: Optional - adds installation directory to system PATH
- **Start Menu Shortcuts**: Creates shortcuts for CLI tool
- **Uninstaller**: Automatic uninstaller with PATH cleanup

## Customization

### Changing Version

Edit `CalendarMcp-Setup.iss`:
```iss
#define MyAppVersion "1.0.0"
```

### Adding Icon

1. Create or obtain a `.ico` file
2. Save as `calendar-mcp-icon.ico` in this directory
3. The installer script already references it

### Modifying PATH Behavior

Edit the `[Tasks]` section in `CalendarMcp-Setup.iss`:
```iss
Name: "addtopath"; Description: "Add Calendar MCP to system PATH"; Flags: checkedonce
```

## Testing

1. Build the installer
2. Test on a clean Windows VM or machine
3. Verify:
   - Installation completes successfully
   - PATH is added (if selected)
   - CLI and Server executables work
   - Uninstaller removes all files and PATH entry

## Troubleshooting

**Error: "Can't find source files"**
- Ensure release binaries are built and in `../release/calendar-mcp-win-x64/`

**Error: "Invalid icon file"**
- Comment out the `SetupIconFile` line if you don't have an icon:
  ```iss
  ; SetupIconFile=calendar-mcp-icon.ico
  ```

**Installer requires admin privileges**
- This is expected - PATH modification requires admin rights
- Users must run installer as administrator

## Support

For issues with the installer:
- [GitHub Issues](https://github.com/rockfordlhotka/calendar-mcp/issues)
- [Installation Documentation](../docs/INSTALLATION.md)
