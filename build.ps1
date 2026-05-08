# FidelSec Forensic Imager — Build Script
# Builds a self-contained portable executable for Windows x64
#
# Usage:
#   .\build.ps1              # Debug build
#   .\build.ps1 -Release     # Release (optimized)
#   .\build.ps1 -SelfContained  # Includes .NET runtime (portable)

param(
    [switch]$Release,
    [switch]$SelfContained,
    [switch]$Test
)

$ErrorActionPreference = "Stop"

$config = if ($Release) { "Release" } else { "Debug" }
$sc     = if ($SelfContained) { "true" } else { "false" }
$outDir = Join-Path $PSScriptRoot "build\$config"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  FidelSec Forensic Imager — Build" -ForegroundColor Cyan
Write-Host "  Configuration : $config" -ForegroundColor Cyan
Write-Host "  Self-contained: $sc" -ForegroundColor Cyan
Write-Host "  Output        : $outDir" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# Restore packages
Write-Host "[1/4] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore "$PSScriptRoot\FidelSec.sln" --runtime win-x64
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# Run tests if requested
if ($Test) {
    Write-Host "[2/4] Running unit tests..." -ForegroundColor Yellow
    dotnet test "$PSScriptRoot\tests\FidelSec.Tests\FidelSec.Tests.csproj" `
        --configuration $config `
        --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
} else {
    Write-Host "[2/4] Skipping tests (use -Test to enable)" -ForegroundColor DarkGray
}

# Build
Write-Host "[3/4] Building solution..." -ForegroundColor Yellow
dotnet build "$PSScriptRoot\FidelSec.sln" `
    --configuration $config `
    --runtime win-x64 `
    --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# Publish UI project
Write-Host "[4/4] Publishing UI project..." -ForegroundColor Yellow

$publishArgs = @(
    "publish"
    "$PSScriptRoot\src\FidelSec.UI\FidelSec.UI.csproj"
    "--configuration", $config
    "--runtime", "win-x64"
    "--output", $outDir
    "--no-restore"
    if ($SelfContained) { "--self-contained", "true" } else { "--self-contained", "false" }
    if ($Release) { "-p:PublishSingleFile=true" }
)

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Build complete!" -ForegroundColor Green
Write-Host "  Output: $outDir" -ForegroundColor Green
Write-Host ""
Write-Host "  IMPORTANT: Run FidelSec.UI.exe as Administrator" -ForegroundColor Yellow
Write-Host "  Raw disk access requires elevated privileges." -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Green
