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

# A running server locks its DLLs - stop instances started from this output folder. MCP clients started by the repo's
# .mcp.json use the relative path "artifacts/twincat-mcp/TwinCatMcp.Server.dll", so relative command lines count too.
$full = [IO.Path]::GetFullPath($Output)
$relative = "artifacts\twincat-mcp\TwinCatMcp.Server.dll"
Get-CimInstance Win32_Process -Filter "Name='TwinCatMcp.Server.exe' OR Name='dotnet.exe'" |
    Where-Object {
        $cmd = if ($_.CommandLine) { $_.CommandLine.Replace('/', '\') } else { "" }
        $cmd.IndexOf($full, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ($cmd.IndexOf($relative, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $cmd -notmatch '[A-Za-z]:\\')
    } |
    ForEach-Object { Write-Host "Stopping running server (PID $($_.ProcessId))"; Stop-Process -Id $_.ProcessId -Force }
# Give Windows a moment to release the file handles of the stopped processes.
Start-Sleep -Seconds 1

dotnet publish $project -c $Configuration -o $Output --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "Published to $full"
