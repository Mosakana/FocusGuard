# FocusGuard Build & Package Script
# Run from the repository root: powershell -File installer/build.ps1

param(
    [switch]$SkipBuild,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$PublishDir = Join-Path $RepoRoot "publish"
$OutputDir = Join-Path $RepoRoot "output"

Write-Host "=== FocusGuard Build Script ===" -ForegroundColor Cyan

# Step 1: Clean
if (-not $SkipBuild) {
    Write-Host "`n[1/4] Cleaning previous build..." -ForegroundColor Yellow
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
    New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    # Step 2: Build and test
    Write-Host "`n[2/4] Building solution..." -ForegroundColor Yellow
    dotnet build "$RepoRoot\FocusGuard.sln" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }

    Write-Host "`n[3/4] Running tests..." -ForegroundColor Yellow
    dotnet test "$RepoRoot\FocusGuard.sln" -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

    # Step 3: Publish
    Write-Host "`n[4/4] Publishing..." -ForegroundColor Yellow
    dotnet publish "$RepoRoot\src\FocusGuard.App\FocusGuard.App.csproj" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "App publish failed" }

    dotnet publish "$RepoRoot\src\FocusGuard.Watchdog\FocusGuard.Watchdog.csproj" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "Watchdog publish failed" }

    Write-Host "`nPublished files:" -ForegroundColor Green
    Get-ChildItem $PublishDir -Filter "*.exe" | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1MB, 1)) MB)" }
}

# Step 4: Build installer
if (-not $SkipInstaller) {
    $iscc = Get-Command "ISCC" -ErrorAction SilentlyContinue
    if ($null -eq $iscc) {
        # Try common install locations
        $isccPaths = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
        )
        foreach ($path in $isccPaths) {
            if (Test-Path $path) {
                $iscc = @{ Source = $path }
                break
            }
        }
    }

    if ($null -ne $iscc) {
        $isccPath = if ($iscc -is [System.Management.Automation.CommandInfo]) { $iscc.Source } else { $iscc.Source }
        Write-Host "`nBuilding installer with Inno Setup..." -ForegroundColor Yellow
        & $isccPath "$RepoRoot\installer\FocusGuard.iss"
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
        Write-Host "`nInstaller created:" -ForegroundColor Green
        Get-ChildItem $OutputDir -Filter "*.exe" | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1MB, 1)) MB)" }
    } else {
        Write-Host "`nInno Setup not found. Skipping installer creation." -ForegroundColor Yellow
        Write-Host "Install Inno Setup 6 from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Build complete ===" -ForegroundColor Cyan
