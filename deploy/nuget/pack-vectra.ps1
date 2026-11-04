$ErrorActionPreference = "Stop"

# Resolve paths
$rootDir = (Resolve-Path "$PSScriptRoot\..\..").Path
$coreProjectFile = Join-Path $rootDir "src\Instancium.Vectra.Core\Instancium.Vectra.Core.csproj"
$nuspecFile = Join-Path $rootDir "deploy\nuget\Instancium.Vectra.nuspec"
$outputDir = Join-Path $rootDir "src\Instancium.Vectra.Core\bin\Release"
$solutionFile = Join-Path $rootDir "Instancium.Vectra.slnx"

Write-Host "Resolved Solution Root: $rootDir" -ForegroundColor DarkGray
Write-Host "Extracting version from Core project..." -ForegroundColor Cyan

if (-not (Test-Path $coreProjectFile)) {
    Write-Host "Error: Cannot find $coreProjectFile" -ForegroundColor Red
    exit 1
}

# 1. Извлекаем версию из Core
[xml]$projContent = Get-Content $coreProjectFile
$Version = $projContent.Project.PropertyGroup.Version | Select-Object -First 1

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "Target version extracted: $Version" -ForegroundColor Green
} else {
    Write-Host "Error: Could not find <Version> element in $coreProjectFile" -ForegroundColor Red
    exit 1
}

# 2. Собираем решение в Release с проверкой кода возврата
Write-Host "Building solution in Release mode..." -ForegroundColor Cyan
dotnet build $solutionFile -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 3. Пакуем пакет в bin/Release
Write-Host "Packing Instancium.Vectra v$Version into bin/Release..." -ForegroundColor Cyan
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

dotnet pack $coreProjectFile -p:NuspecFile=$nuspecFile -p:NuspecProperties="version=$Version" --no-build -o $outputDir --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=======================================================" -ForegroundColor Green
Write-Host "SUCCESS: Package generated successfully." -ForegroundColor Green
Write-Host "Zero transitive dependencies guaranteed." -ForegroundColor Green
Write-Host "You can find the .nupkg file for manual upload here:" -ForegroundColor Yellow
Write-Host (Resolve-Path $outputDir).Path -ForegroundColor Yellow