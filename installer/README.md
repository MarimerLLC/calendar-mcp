# Adjutant Installer

This directory contains the Windows installer configuration for Adjutant.

## Files

- **Adjutant-Setup.iss**: Inno Setup script for creating the Windows installer
- **installer-readme.txt**: Information displayed to users during installation
- **adjutant-icon.ico**: Application icon, drawn from `src/Adjutant.HttpServer/wwwroot/favicon.svg` (bitmap entries 16-48 px plus a 256 px PNG)

## Building the Installer

### Prerequisites

1. Install [Inno Setup](https://jrsoftware.org/isinfo.php) (free)
2. Build the Adjutant projects in Release mode
3. Ensure release binaries are in `../release/adjutant-win-x64/`

### Build Process

#### Manual Build

```bash
# 1. Build the projects
dotnet publish src/Adjutant.Cli/Adjutant.Cli.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/Adjutant.StdioServer/Adjutant.StdioServer.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true

# 2. Run Inno Setup compiler
iscc installer/Adjutant-Setup.iss
```

The installer will be created as `release/adjutant-setup-win-x64.exe`

#### Automated Build (GitHub Actions)

The GitHub Actions workflow (`.github/workflows/release.yml`) will automatically build the installer when:
- Changes are pushed to the `release` branch
- A version tag (e.g., `v1.0.0`) is created

The installer will be uploaded as a release asset.

## Installer Features

- **Installation Directory**: Default to `C:\Program Files\Adjutant\`
- **PATH Addition**: Optional - adds installation directory to system PATH
- **Start Menu Shortcuts**: Creates shortcuts for CLI tool
- **Uninstaller**: Automatic uninstaller with PATH cleanup

## Customization

### Changing Version

The version is **not** set in `Adjutant-Setup.iss`. The script reads it from the
built payload at compile time with `GetFileVersion`, so the installer always matches
the version the binaries were actually built with.

To change it, edit `VersionPrefix` in `Directory.Build.props` at the repo root:
```xml
<VersionPrefix>1.5.0</VersionPrefix>
```

Then rebuild the payload so the new version is stamped into the binaries. Compiling
without a payload in `..\release\adjutant-win-x64\` fails with an explicit message
rather than silently producing a wrongly-versioned installer.

### Icon

`adjutant-icon.ico` is the same mark as the admin UI favicon (`src/Adjutant.HttpServer/wwwroot/favicon.svg`,
with `icons/favicon-small.svg` for 16-32 px). If the mark changes, regenerate both icons from those SVGs.

### Modifying PATH Behavior

Edit the `[Tasks]` section in `Adjutant-Setup.iss`:
```iss
Name: "addtopath"; Description: "Add Adjutant to system PATH"; Flags: checkedonce
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
- Ensure release binaries are built and in `../release/adjutant-win-x64/`

**Installer requires admin privileges**
- This is expected - PATH modification requires admin rights
- Users must run installer as administrator

## Support

For issues with the installer:
- [GitHub Issues](https://github.com/MarimerLLC/adjutant/issues)
- [Installation Documentation](../docs/INSTALLATION.md)
