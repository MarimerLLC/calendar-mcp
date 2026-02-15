# Project: Calendar MCP

## Environment Notes

- This project is developed on Windows using Git Bash (MSYS2/MinGW)
- When running kubectl, docker, or other commands that pass Unix-style paths, prefix with `MSYS_NO_PATHCONV=1` to prevent Git Bash from mangling paths (e.g., `/app/data` becoming `C:/Program Files/Git/app/data`)

## Kubernetes

- Namespace: `calendar-mcp`
- Manifests in `k8s/` directory
