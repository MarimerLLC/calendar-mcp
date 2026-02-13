# Calendar MCP HTTP Server - Multi-stage Docker build
# Build: docker build -t calendar-mcp-http .
# Run:   docker run -p 8080:8080 -v calendar-mcp-data:/app/data calendar-mcp-http

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for layer caching
COPY src/CalendarMcp.Core/CalendarMcp.Core.csproj CalendarMcp.Core/
COPY src/CalendarMcp.Auth/CalendarMcp.Auth.csproj CalendarMcp.Auth/
COPY src/CalendarMcp.HttpServer/CalendarMcp.HttpServer.csproj CalendarMcp.HttpServer/

# Restore dependencies
RUN dotnet restore CalendarMcp.HttpServer/CalendarMcp.HttpServer.csproj

# Copy all source files
COPY src/CalendarMcp.Core/ CalendarMcp.Core/
COPY src/CalendarMcp.Auth/ CalendarMcp.Auth/
COPY src/CalendarMcp.HttpServer/ CalendarMcp.HttpServer/

# Build and publish
RUN dotnet publish CalendarMcp.HttpServer/CalendarMcp.HttpServer.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r mcpuser && useradd -r -g mcpuser -d /app -s /sbin/nologin mcpuser

# Create data directories
RUN mkdir -p /app/data/logs /app/data/tokens && \
    chown -R mcpuser:mcpuser /app

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV CALENDAR_MCP_CONFIG=/app/data

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Expose MCP and admin ports
EXPOSE 8080

# Switch to non-root user
USER mcpuser

ENTRYPOINT ["dotnet", "CalendarMcp.HttpServer.dll"]
