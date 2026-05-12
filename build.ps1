# FidelSec Forensic Imager — Build Script
# Builds both the legacy WPF UI (Windows only) and the cross-platform Avalonia UI.
#
# Usage:
#   .\build.ps1              # Debug build (Avalonia + WPF)
#   .\build.ps1 -Release     # Release (optimized)
#   .\build.ps1 -SelfContained  # Includes .NET runtime (portable)
#   .\build.ps1 -Avalonia    # Build only the Avalonia UI
#   .\build.ps1 -WPF         # Build only the legacy WPF UI
#   .\build.ps1 -Test        # Run unit tests

param(
    [switch]$Release,
    [switch]$SelfContained,
    [switch]$Test,
    [switch]$Avalonia,
    [switch]$WPF
)

$ErrorActionPreference = "Stop"

$config   = if ($Release) { "Release" } else { "Debug" }
$outDir   = Join-Path $PSScriptRoot "build\$config"
$outWpf   = Join-Path $outDir "WPF"
$outAv    = Join-Path $outDir "Avalonia"

# Default: build both unless a specific flag is given
$buildWpf     = -not $Avalonia
$buildAvalonia = -not $WPF

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  FidelSec Forensic Imager — Build (Windows)" -ForegroundColor Cyan
Write-Host "  Configuration : $config" -ForegroundColor Cyan
Write-Host "  Self-contained: $($SelfContained.IsPresent)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# Restore packages
Write-Host "[1/4] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore "$PSScriptRoot\FidelSec.sln"
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

# Build solution
Write-Host "[3/4] Building solution..." -ForegroundColor Yellow
dotnet build "$PSScriptRoot\FidelSec.sln" --configuration $config --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# Publish
Write-Host "[4/4] Publishing..." -ForegroundColor Yellow

$scArg = if ($SelfContained) { "--self-contained", "true" } else { "--self-contained", "false" }

# ── Avalonia UI (cross-platform, Windows x64) ────────────────────────────────
if ($buildAvalonia) {
    Write-Host "  -> Avalonia UI (win-x64)" -ForegroundColor Yellow
    $avArgs = @(
        "publish"
        "$PSScriptRoot\src\FidelSec.UI.Avalonia\FidelSec.UI.Avalonia.csproj"
        "--configuration", $config
        "--runtime", "win-x64"
        "--output", $outAv
        "--no-restore"
    ) + $scArg
    if ($Release) { $avArgs += "-p:PublishSingleFile=true" }
    dotnet @avArgs
    if ($LASTEXITCODE -ne 0) { throw "Avalonia publish failed" }
}

# ── Legacy WPF UI (Windows only) ─────────────────────────────────────────────
if ($buildWpf) {
    Write-Host "  -> WPF UI (win-x64)" -ForegroundColor Yellow
    $wpfArgs = @(
        "publish"
        "$PSScriptRoot\src\FidelSec.UI\FidelSec.UI.csproj"
        "--configuration", $config
        "--runtime", "win-x64"
        "--output", $outWpf
        "--no-restore"
    ) + $scArg
    if ($Release) { $wpfArgs += "-p:PublishSingleFile=true" }
    dotnet @wpfArgs
    if ($LASTEXITCODE -ne 0) { throw "WPF publish failed" }
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Build complete!" -ForegroundColor Green
if ($buildAvalonia) { Write-Host "  Avalonia : $outAv" -ForegroundColor Green }
if ($buildWpf)      { Write-Host "  WPF      : $outWpf" -ForegroundColor Green }
Write-Host ""
Write-Host "  IMPORTANT: Run as Administrator (raw disk access required)" -ForegroundColor Yellow
Write-Host "  Start-Process '$outAv\FidelSec.UI.Avalonia.exe' -Verb RunAs" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Green


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
