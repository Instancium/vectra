# Syncs PublicAPI.Unshipped.txt for the v2 engine projects (Core, Search, Storage).
# New or changed public symbols are added plainly; symbols that exist in the v1 snapshot
# (PublicAPI.Shipped.txt) but no longer exist in source are acknowledged with *REMOVED*.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sync-public-api.ps1
$ErrorActionPreference = 'Continue'
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$newLine = [Environment]::NewLine
$header = '#nullable enable'
$quote = [char]39
$markerAdded = 'RS0016: Symbol ' + $quote
$markerRemoved = 'RS0017: Symbol ' + $quote

$projects = @(
    (Join-Path $repoRoot 'src\Instancium.Vectra.Core\Instancium.Vectra.Core.csproj'),
    (Join-Path $repoRoot 'src\Instancium.Vectra.Search\Instancium.Vectra.Search.csproj'),
    (Join-Path $repoRoot 'src\Instancium.Vectra.Storage\Instancium.Vectra.Storage.csproj')
)

foreach ($projectPath in $projects) {
    $directory = Split-Path -Parent $projectPath
    $unshipped = Join-Path $directory 'PublicAPI.Unshipped.txt'
    Write-Host ('=== ' + (Split-Path -Leaf $projectPath) + ' ===')

    $output = & dotnet build $projectPath -v:m -nologo -p:BuildProjectReferences=false 2>&1 | Out-String

    $added = @()
    $removed = @()
    foreach ($rawLine in ($output -split [char]10)) {
        $text = [string]$rawLine
        $isRemoved = $false
        $marker = $null
        if ($text.Contains($markerAdded)) {
            $marker = $markerAdded
        }
        elseif ($text.Contains($markerRemoved)) {
            $marker = $markerRemoved
            $isRemoved = $true
        }

        if ($null -eq $marker) { continue }
        $index = $text.IndexOf($marker, [StringComparison]::Ordinal)
        $rest = $text.Substring($index + $marker.Length)
        $end = $rest.IndexOf($quote)
        if ($end -le 0) { continue }
        $symbol = $rest.Substring(0, $end)
        if ($isRemoved) { $removed += $symbol } else { $added += $symbol }
    }

    $added = @($added | Sort-Object -Unique)
    $removed = @($removed | Sort-Object -Unique)

    $lines = @()
    $lines += $added
    foreach ($symbol in $removed) { $lines += ('*REMOVED*' + $symbol) }

    $content = $header + $newLine
    if ($lines.Count -gt 0) { $content += ($lines -join $newLine) + $newLine }
    [System.IO.File]::WriteAllText($unshipped, $content)
    Write-Host ('  added: ' + $added.Count + ', removed: ' + $removed.Count)
}

Write-Host '=== verify: full rebuild, expect zero RS0016/RS0017 ==='
& dotnet build (Join-Path $repoRoot 'Instancium.Vectra.slnx') -v:m -nologo