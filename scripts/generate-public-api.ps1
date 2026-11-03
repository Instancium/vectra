# Generates PublicAPI.Shipped.txt for every src project from RS0016 diagnostics.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\generate-public-api.ps1
$ErrorActionPreference = 'Continue'
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$solution = Join-Path $repoRoot 'Instancium.Vectra.slnx'
$newLine = [Environment]::NewLine
$header = '#nullable enable'
$quote = [char]39
$marker = 'RS0016: Symbol ' + $quote

Write-Host '=== warm-up: full solution build ==='
& dotnet build $solution -v:q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'warm-up build failed' -ForegroundColor Red
    exit 1
}

$projects = Get-ChildItem -Path (Join-Path $repoRoot 'src') -Filter '*.csproj' -Recurse | Sort-Object Name
foreach ($project in $projects) {
    $directory = $project.DirectoryName
    $shipped = Join-Path $directory 'PublicAPI.Shipped.txt'
    $unshipped = Join-Path $directory 'PublicAPI.Unshipped.txt'
    Write-Host ('=== ' + $project.Name + ' ===')
    if (-not (Test-Path $shipped)) {
        [System.IO.File]::WriteAllText($shipped, $header + $newLine)
    }
    if (-not (Test-Path $unshipped)) {
        [System.IO.File]::WriteAllText($unshipped, $header + $newLine)
    }
    [System.IO.File]::WriteAllText($shipped, $header + $newLine)

    $rawLines = & dotnet build $project.FullName -v:m -nologo -p:BuildProjectReferences=false 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'project build failed' -ForegroundColor Red
        exit 1
    }

    $symbols = @()
    foreach ($rawLine in $rawLines) {
        $text = [string]$rawLine
        $index = $text.IndexOf($marker, [StringComparison]::Ordinal)
        if ($index -lt 0) { continue }
        $rest = $text.Substring($index + $marker.Length)
        $end = $rest.IndexOf($quote)
        if ($end -le 0) { continue }
        $symbols += $rest.Substring(0, $end)
    }
    $uniqueSymbols = @($symbols | Sort-Object -Unique)
    if ($uniqueSymbols.Count -eq 0) {
        Write-Host '  no public API (internal-only project)'
        continue
    }
    $content = $header + $newLine + ($uniqueSymbols -join $newLine) + $newLine
    [System.IO.File]::WriteAllText($shipped, $content)
    Write-Host ('  symbols written: ' + $uniqueSymbols.Count)
}

Write-Host '=== verify: full rebuild, expect zero RS0016 ==='
& dotnet build $solution -v:m -nologo