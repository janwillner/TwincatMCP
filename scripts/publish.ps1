<#
.SYNOPSIS
  Builds the TwinCAT MCP server into artifacts\twincat-mcp (framework-dependent, .NET 10, x64).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#>
param(
    [string]$Configuration = "Release",
    [string]$Output = (Join-Path $PSScriptRoot "..\artifacts\twincat-mcp")
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\src\TwinCatMcp.Server\TwinCatMcp.Server.csproj"

# A running server locks its DLLs - stop instances started from this output folder.
$full = [IO.Path]::GetFullPath($Output)
Get-CimInstance Win32_Process -Filter "Name='TwinCatMcp.Server.exe' OR Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Contains($full) } |
    ForEach-Object { Write-Host "Stopping running server (PID $($_.ProcessId))"; Stop-Process -Id $_.ProcessId -Force }

dotnet publish $project -c $Configuration -o $Output --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "Published to $full"
