{
  description = "Calendar MCP - Unified email and calendar access for AI assistants";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};

        dotnetSdk = pkgs.dotnet-sdk_9;
        dotnetRuntime = pkgs.dotnet-runtime_9;

        projectName = "CalendarMcp";
        version = "0.1.0";

      in {
        packages = {
          # MCP Server - the main package
          default = pkgs.buildDotnetModule {
            pname = "calendar-mcp-server";
            inherit version;

            src = ./src;

            projectFile = "CalendarMcp.StdioServer/CalendarMcp.StdioServer.csproj";
            executables = [ "CalendarMcp.StdioServer" ];

            dotnet-sdk = dotnetSdk;
            dotnet-runtime = dotnetRuntime;

            nugetDeps = ./deps-server.json;
            runtimeDeps = [ pkgs.icu ];

            meta = with pkgs.lib; {
              description = "MCP server for unified email and calendar access";
              homepage = "https://github.com/Veraticus/calendar-mcp";
              license = licenses.mit;
              platforms = platforms.all;
            };
          };

          # CLI for account management - separate to avoid version conflicts
          cli = pkgs.buildDotnetModule {
            pname = "calendar-mcp-cli";
            inherit version;

            src = ./src;

            projectFile = "CalendarMcp.Cli/CalendarMcp.Cli.csproj";
            executables = [ "CalendarMcp.Cli" ];

            dotnet-sdk = dotnetSdk;
            dotnet-runtime = dotnetRuntime;

            nugetDeps = ./deps-cli.json;
            runtimeDeps = [ pkgs.icu ];

            meta = with pkgs.lib; {
              description = "CLI for managing Calendar MCP accounts";
              homepage = "https://github.com/Veraticus/calendar-mcp";
              license = licenses.mit;
              platforms = platforms.all;
            };
          };
        };

        devShells.default = pkgs.mkShell {
          buildInputs = [
            dotnetSdk
            pkgs.nuget-to-json
          ];

          shellHook = ''
            echo "Calendar MCP development shell"
            echo "  dotnet version: $(dotnet --version)"
            echo ""
            echo "To regenerate deps.json:"
            echo "  cd src && dotnet restore && nuget-to-json . > ../deps.json"
          '';
        };
      }
    ) // {
      nixosModules.default = { config, lib, pkgs, ... }:
        let
          cfg = config.services.calendar-mcp;
        in {
          options.services.calendar-mcp = {
            enable = lib.mkEnableOption "Calendar MCP server";

            package = lib.mkOption {
              type = lib.types.package;
              default = self.packages.${pkgs.system}.default;
              description = "The calendar-mcp package to use";
            };

            dataDir = lib.mkOption {
              type = lib.types.path;
              default = "/var/lib/calendar-mcp";
              description = "Directory for calendar-mcp data";
            };

            user = lib.mkOption {
              type = lib.types.str;
              default = "calendar-mcp";
              description = "User to run calendar-mcp as";
            };

            group = lib.mkOption {
              type = lib.types.str;
              default = "calendar-mcp";
              description = "Group to run calendar-mcp as";
            };
          };

          config = lib.mkIf cfg.enable {
            users.users.${cfg.user} = {
              isSystemUser = true;
              group = cfg.group;
              home = cfg.dataDir;
              createHome = true;
            };

            users.groups.${cfg.group} = {};

            systemd.services.calendar-mcp = {
              description = "Calendar MCP Server";
              wantedBy = [ "multi-user.target" ];
              after = [ "network.target" ];

              serviceConfig = {
                Type = "simple";
                User = cfg.user;
                Group = cfg.group;
                ExecStart = "${cfg.package}/bin/CalendarMcp.StdioServer";
                Restart = "on-failure";
                RestartSec = 5;

                # Hardening
                NoNewPrivileges = true;
                ProtectSystem = "strict";
                ProtectHome = true;
                PrivateTmp = true;
                ReadWritePaths = [ cfg.dataDir ];
              };

              environment = {
                HOME = cfg.dataDir;
                XDG_DATA_HOME = "${cfg.dataDir}/.local/share";
              };
            };
          };
        };
    };
}
