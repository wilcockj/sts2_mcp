# STS2 Mod Build Script
# Usage:
#   .\build.ps1          - Build and deploy
#   .\build.ps1 -Run     - Build, deploy, and launch game

param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$ProjectDir = $PSScriptRoot
$LocalPropsPath = Join-Path $ProjectDir "local.props"

$STS2GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
$MegaDotExe = $null
$ModName = "sts2mcp"

if (Test-Path $LocalPropsPath) {
    [xml]$props = Get-Content $LocalPropsPath
    $STS2GamePath = $props.Project.PropertyGroup.STS2GamePath
    $MegaDotExe = $props.Project.PropertyGroup.MegaDotExe
    if ($props.Project.PropertyGroup.ModName) {
        $ModName = $props.Project.PropertyGroup.ModName
    }
}

$GameExe = Join-Path $STS2GamePath "SlayTheSpire2.exe"

Write-Host "Building sts2mcp..." -ForegroundColor Cyan

if ($Run) {
    Write-Host "Stopping existing SlayTheSpire2..." -ForegroundColor Cyan
    Stop-Process -Name "SlayTheSpire2" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

dotnet build --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build succeeded." -ForegroundColor Green

if ($Run) {
    if (-not (Test-Path $GameExe)) {
        Write-Host "Game not found at: $GameExe" -ForegroundColor Red
        exit 1
    }

    if ($MegaDotExe -and (Test-Path $MegaDotExe)) {
        $MegaDotName = [System.IO.Path]::GetFileNameWithoutExtension($MegaDotExe)
        $MegaDotRunning = Get-Process -Name $MegaDotName -ErrorAction SilentlyContinue
        if (-not $MegaDotRunning) {
            Write-Host "Opening MegaDot..." -ForegroundColor Cyan
            Start-Process $MegaDotExe -ArgumentList "--editor","--path","`"$ProjectDir`""
            Start-Sleep -Seconds 5
        } else {
            Write-Host "MegaDot already running." -ForegroundColor Cyan
        }
    }

    Write-Host "Launching Slay the Spire 2 via Steam with debug..." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "In MegaDot:" -ForegroundColor Yellow
    Write-Host "  1. Debug -> Attach to Process -> SlayTheSpire2" -ForegroundColor Yellow
    Write-Host ""

    $SteamExe = "C:\Program Files (x86)\Steam\Steam.exe"
    Start-Process $SteamExe -ArgumentList "-applaunch","2868840","--remote-debug","tcp://127.0.0.1:6007"

    Write-Host "Game launched." -ForegroundColor Green
}
