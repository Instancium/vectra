# ==============================================================================
# Script: pack-vectra-sdk.ps1
# Description: Self-contained packaging pipeline for Instancium.Vectra.Sdk.
#              Outputs package strictly to src/Instancium.Vectra.Sdk/bin/Release
#              to prevent committing binary artifacts into Git.
# ==============================================================================

$ErrorActionPreference = "Stop"

# 1. Resolve repository root by walking up from script directory
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$currentDir = (Resolve-Path $scriptDir).Path
$rootDir = $null

while ($currentDir) {
    $probePath = Join-Path $currentDir "src\Instancium.Vectra.Sdk\Instancium.Vectra.Sdk.csproj"
    if (Test-Path $probePath) {
        $rootDir = $currentDir
        break
    }
    $parent = Split-Path -Parent $currentDir
    if ($parent -eq $currentDir) { break }
    $currentDir = $parent
}

if (-not $rootDir) {
    Write-Host "Error: Could not locate repository root containing 'src\Instancium.Vectra.Sdk\Instancium.Vectra.Sdk.csproj'." -ForegroundColor Red
    exit 1
}

$sdkProjectFile = Join-Path $rootDir "src\Instancium.Vectra.Sdk\Instancium.Vectra.Sdk.csproj"
$outputDir = Join-Path $rootDir "src\Instancium.Vectra.Sdk\bin\Release"

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "       Instancium.Vectra.Sdk Packaging Pipeline        " -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "Repository Root: $rootDir" -ForegroundColor DarkGray
Write-Host "Project File:    $sdkProjectFile" -ForegroundColor DarkGray
Write-Host "Output Directory: $outputDir" -ForegroundColor DarkGray

# 2. Extract package version from csproj
$sdkContent = Get-Content $sdkProjectFile -Raw
$versionMatch = [regex]::Match($sdkContent, '<Version>(.*?)</Version>')

if ($versionMatch.Success) {
    $version = $versionMatch.Groups[1].Value.Trim()
    Write-Host "Target Version:  $version" -ForegroundColor Green
} else {
    Write-Host "Error: Could not find <Version> element inside $sdkProjectFile" -ForegroundColor Red
    exit 1
}

# 3. Ensure target bin/Release directory exists
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# 4. Compile internal project dependencies in Release mode
Write-Host "`n[1/4] Compiling internal dependencies (Release)..." -ForegroundColor Cyan
dotnet build $sdkProjectFile -c Release --nologo -v minimal

# 5. Pack SDK into src/Instancium.Vectra.Sdk/bin/Release
Write-Host "`n[2/4] Packaging Instancium.Vectra.Sdk v$version..." -ForegroundColor Cyan
dotnet pack $sdkProjectFile -c Release --no-build -p:PackageVersion=$version -o $outputDir --nologo

# 6. Purge local NuGet machine cache to prevent stale rollbacks
Write-Host "`n[3/4] Purging local NuGet cache for version $version..." -ForegroundColor Cyan
$userProfile = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile)
$packageCacheDir = Join-Path $userProfile ".nuget\packages\instancium.vectra.sdk\$version"

if (Test-Path $packageCacheDir) {
    Remove-Item -Path $packageCacheDir -Recurse -Force
    Write-Host "Cache cleared: $packageCacheDir" -ForegroundColor Yellow
} else {
    Write-Host "No conflicting cache entries found." -ForegroundColor DarkGray
}

# 7. Final status verification
Write-Host "`n[4/4] Verifying generated package..." -ForegroundColor Cyan
$packageFile = Join-Path $outputDir "Instancium.Vectra.Sdk.$version.nupkg"

if (Test-Path $packageFile) {
    Write-Host "=======================================================" -ForegroundColor Green
    Write-Host "SUCCESS: Package generated successfully." -ForegroundColor Green
    Write-Host "Nupkg location: $packageFile" -ForegroundColor Green
    Write-Host "`nConfigure this feed directory in Visual Studio Package Sources:" -ForegroundColor Yellow
    Write-Host (Resolve-Path $outputDir).Path -ForegroundColor Yellow
    Write-Host "=======================================================" -ForegroundColor Green
} else {
    Write-Host "Error: Package was not generated at expected path: $packageFile" -ForegroundColor Red
    exit 1
}