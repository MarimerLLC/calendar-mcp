# Adjutant HTTP Server - Multi-stage Docker build
# Build: docker build -t adjutant .
# Run:   docker run -p 8080:8080 -v adjutant-data:/app/data adjutant
# Release (the k8s nodes are amd64; works from an arm64 machine too):
#        docker buildx build --platform linux/amd64 -t rockylhotka/adjutant:<version> --push .

# Stage 1: Build. Runs on the build machine's own platform and cross-compiles for the
# target, because the .NET SDK crashes under QEMU emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy project files for layer caching
# Directory.Build.props lives at the repo root and is auto-imported by MSBuild
# from the parent of each .csproj, so we drop it next to the projects in /src.
COPY Directory.Build.props ./
COPY src/Adjutant.Core/Adjutant.Core.csproj Adjutant.Core/
COPY src/Adjutant.Auth/Adjutant.Auth.csproj Adjutant.Auth/
COPY src/Adjutant.HttpServer/Adjutant.HttpServer.csproj Adjutant.HttpServer/

# Restore dependencies
RUN dotnet restore Adjutant.HttpServer/Adjutant.HttpServer.csproj -a $TARGETARCH

# Copy all source files
COPY src/Adjutant.Core/ Adjutant.Core/
COPY src/Adjutant.Auth/ Adjutant.Auth/
COPY src/Adjutant.HttpServer/ Adjutant.HttpServer/

# Build and publish for the target architecture
RUN dotnet publish Adjutant.HttpServer/Adjutant.HttpServer.csproj \
    -c Release \
    -a $TARGETARCH \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

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
ENV ADJUTANT_CONFIG=/app/data

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Expose MCP and admin ports
EXPOSE 8080

# Switch to non-root user
USER mcpuser

ENTRYPOINT ["dotnet", "Adjutant.HttpServer.dll"]
