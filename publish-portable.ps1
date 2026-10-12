<#
.SYNOPSIS
    Builds and publishes ConfigManager as a Single-File Portable Executable for Windows x64.
.PARAMETER SelfContained
    Whether to bundle the .NET Runtime inside the executable (default: true).
    Set to $false for a lightweight build (requires .NET 9 installed on target machine).
#>
param(
    [switch]$FrameworkDependent = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ConfigManager - Single-File Portable Executable Builder  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$isSelfContained = -not $FrameworkDependent
$outputFolder = if ($isSelfContained) { "publish\ConfigManager-Portable-SelfContained" } else { "publish\ConfigManager-Portable-FrameworkDependent" }

Write-Host "Target OS        : win-x64" -ForegroundColor Gray
Write-Host "Self-Contained   : $isSelfContained" -ForegroundColor Gray
Write-Host "Output Directory : $outputFolder" -ForegroundColor Gray
Write-Host "Starting publish process..." -ForegroundColor Yellow

$publishArgs = @(
    "publish", "ConfigManager.csproj",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $(if ($isSelfContained) { "true" } else { "false" }),
    "-p:PublishSingleFile=true",
    "-o", $outputFolder
)

dotnet @publishArgs

if ($LASTEXITCODE -eq 0) {
    $exePath = Join-Path $outputFolder "ConfigManager.exe"
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host ""
        Write-Host "BUILD SUCCESSFUL!" -ForegroundColor Green
        Write-Host "Portable Executable Created:" -ForegroundColor White
        Write-Host "   Path : $exePath" -ForegroundColor Cyan
        Write-Host "   Size : $sizeMB MB" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "You can now copy and run '$exePath' on any 64-bit Windows machine!" -ForegroundColor Green
    }
} else {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
