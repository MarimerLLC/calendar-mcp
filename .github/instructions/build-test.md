# Build, Test, and Environment Setup

## Prerequisites

### Building from Source

**Required:**
- .NET 10 SDK ([Download](https://dotnet.microsoft.com/download/dotnet/10.0))
- Git

**Optional (for development):**
- Visual Studio 2022 (17.12+) or VS Code with C# Dev Kit
- Docker (for containerized deployment)
- Kubernetes (for k8s deployment)

### Using Pre-built Binaries

Pre-built binaries are self-contained and **do not** require the .NET runtime to be installed.

Download from [Releases](https://github.com/MarimerLLC/calendar-mcp/releases).

## Building the Project

### Build All Projects

```bash
cd /home/runner/work/calendar-mcp/calendar-mcp
dotnet build
```

This builds all projects in the solution:
- `CalendarMcp.Core` - Core library
- `CalendarMcp.StdioServer` - MCP stdio server
- `CalendarMcp.HttpServer` - HTTP server with Blazor UI
- `CalendarMcp.Cli` - CLI tool for account management
- `CalendarMcp.Auth` - Authentication middleware

### Build Specific Project

```bash
dotnet build src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj
```

### Build for Release

```bash
dotnet build -c Release
```

### Publish Self-Contained Executable

For distribution without requiring .NET runtime:

**Windows (x64):**
```bash
dotnet publish src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true
```

**Linux (x64):**
```bash
dotnet publish src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true
```

**macOS (ARM64 - M1/M2/M3):**
```bash
dotnet publish src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true
```

**macOS (x64 - Intel):**
```bash
dotnet publish src/CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj \
  -c Release \
  -r osx-x64 \
  --self-contained true \
  -p:PublishSingleFile=true
```

Published executables will be in:
```
src/CalendarMcp.StdioServer/bin/Release/net10.0/{runtime}/publish/
```

## Testing

### Current Testing Approach

**Note:** There is currently no dedicated test project in the repository. Testing is primarily done through:

1. **Manual testing** with the CLI tool
2. **Integration testing** with MCP clients (Claude Desktop, VS Code)
3. **Running the servers** and verifying tool behavior

### Future Testing Infrastructure

When adding tests, follow these patterns:

**Create test project:**
```bash
dotnet new xunit -n CalendarMcp.Core.Tests -o tests/CalendarMcp.Core.Tests
dotnet add tests/CalendarMcp.Core.Tests/CalendarMcp.Core.Tests.csproj reference src/CalendarMcp.Core/CalendarMcp.Core.csproj
dotnet sln add tests/CalendarMcp.Core.Tests/CalendarMcp.Core.Tests.csproj
```

**Run tests:**
```bash
dotnet test
```

**Run tests with coverage:**
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Manual Testing

**Test the stdio server:**
```bash
dotnet run --project src/CalendarMcp.StdioServer
```

**Test the CLI tool:**
```bash
dotnet run --project src/CalendarMcp.Cli -- list-accounts
dotnet run --project src/CalendarMcp.Cli -- add-account
```

**Test the HTTP server:**
```bash
dotnet run --project src/CalendarMcp.HttpServer
# Navigate to http://localhost:5000
```

## Environment Setup

### Windows

**Development Environment:**
- Git Bash (MSYS2/MinGW) is used for development
- When running kubectl, docker, or commands that pass Unix paths, prefix with `MSYS_NO_PATHCONV=1`
  ```bash
  MSYS_NO_PATHCONV=1 docker run -v /app/data:/data myimage
  ```

**Configuration Location:**
```powershell
%LOCALAPPDATA%\CalendarMcp\appsettings.json
# Typically: C:\Users\{username}\AppData\Local\CalendarMcp\appsettings.json
```

**Token Storage:**
- Microsoft: DPAPI encrypted storage in user profile
- Google: Encrypted file storage in `%LOCALAPPDATA%\CalendarMcp\tokens\{accountId}\`

### Linux

**Configuration Location:**
```bash
~/.local/share/CalendarMcp/appsettings.json
# Or: $XDG_DATA_HOME/CalendarMcp/appsettings.json
```

**Token Storage:**
- Microsoft: Encrypted file storage (no DPAPI on Linux)
- Google: Encrypted file storage in `~/.local/share/CalendarMcp/tokens/{accountId}/`

**Environment Variables:**
```bash
export CALENDAR_MCP_Telemetry__Enabled=true
export CALENDAR_MCP_Accounts__0__Id=work-account
```

### macOS

**Configuration Location:**
```bash
~/Library/Application Support/CalendarMcp/appsettings.json
```

**Token Storage:**
- Microsoft: Keychain storage (macOS-specific)
- Google: Encrypted file storage in `~/Library/Application Support/CalendarMcp/tokens/{accountId}/`

## Configuration File Setup

### Initial Configuration

1. **Run the CLI tool to set up first account:**
   ```bash
   dotnet run --project src/CalendarMcp.Cli -- add-account
   ```

2. **Or manually create `appsettings.json`:**

   Get the config file path:
   ```bash
   dotnet run --project src/CalendarMcp.Cli -- config-path
   ```

   Create the file with this structure:
   ```json
   {
     "CalendarMcp": {
       "Accounts": [
         {
           "id": "work-m365",
           "provider": "microsoft365",
           "displayName": "Work Account",
           "enabled": true,
           "domains": ["company.com"],
           "providerConfig": {
             "tenantId": "your-tenant-id",
             "clientId": "your-client-id"
           }
         }
       ],
       "Telemetry": {
         "Enabled": true,
         "MinimumLevel": "Information"
       }
     }
   }
   ```

### Environment Variable Override

Any configuration can be overridden with environment variables:

```bash
# Format: CALENDAR_MCP_{Section}__{Property}
export CALENDAR_MCP_Telemetry__Enabled=false
export CALENDAR_MCP_Telemetry__MinimumLevel=Debug
```

## Docker

### Build Docker Image

```bash
docker build -t calendar-mcp:latest .
```

### Run with Docker Compose

```bash
docker-compose up -d
```

Configuration is mounted from host:
```yaml
volumes:
  - ./appsettings.json:/app/appsettings.json:ro
```

## Kubernetes

### Deploy to Kubernetes

**Namespace:**
```bash
kubectl create namespace calendar-mcp
```

**Deploy manifests:**
```bash
kubectl apply -f k8s/
```

**Check status:**
```bash
kubectl get pods -n calendar-mcp
kubectl logs -n calendar-mcp deployment/calendar-mcp
```

## Common Build Issues

### Issue: .NET 10 SDK not found

**Solution:** Install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0

### Issue: NuGet package restore fails

**Solution:**
```bash
dotnet nuget locals all --clear
dotnet restore
```

### Issue: Build fails with "CS0234: The type or namespace name does not exist"

**Solution:** Ensure all project references are correct and run:
```bash
dotnet clean
dotnet restore
dotnet build
```

### Issue: Path conversion errors on Windows (Git Bash)

**Solution:** Use `MSYS_NO_PATHCONV=1` prefix:
```bash
MSYS_NO_PATHCONV=1 docker run -v /app:/app myimage
```

## IDE Setup

### Visual Studio 2022

1. Open `calendar-mcp.slnx` solution file
2. Set startup project (StdioServer, HttpServer, or Cli)
3. F5 to run with debugging

### VS Code

1. Install C# Dev Kit extension
2. Open folder in VS Code
3. Use "Run and Debug" panel to start projects

**Recommended extensions:**
- C# Dev Kit
- C#
- NuGet Package Manager
- REST Client (for testing HTTP server)

### Rider

1. Open solution folder
2. Rider will auto-detect .NET projects
3. Run configurations are auto-created

## Troubleshooting

### Check .NET Version

```bash
dotnet --version  # Should be 10.x
dotnet --list-sdks
```

### Verify Project Build

```bash
dotnet build --verbosity detailed
```

### Check Configuration

```bash
dotnet run --project src/CalendarMcp.Cli -- config-path
cat $(dotnet run --project src/CalendarMcp.Cli -- config-path)
```

### View Logs

Logs are written to console with OpenTelemetry. To increase verbosity:

**In appsettings.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  }
}
```

**Via environment variable:**
```bash
export Logging__LogLevel__Default=Debug
```
