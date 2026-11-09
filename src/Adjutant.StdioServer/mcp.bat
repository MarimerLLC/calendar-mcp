@echo off
REM Get the directory where this batch file is located
set SCRIPT_DIR=%~dp0

REM Run the MCP server from the bin/Debug/net9.0 directory
"%SCRIPT_DIR%bin\Debug\net9.0\adjutant.stdioserver.exe" %*
