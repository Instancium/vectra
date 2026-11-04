# Syncs PublicAPI.Unshipped.txt for the v2 engine projects (Core, Search, Storage).
# Fixes the overwrite bug by merging new symbols with existing file contents.
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
    Write-Host ("=== " + (Split-Path -Leaf $projectPath) + " ===")

    # 1. Считываем текущее содержимое файла (сохраняем историю)
    $existing = @()
    if (Test-Path $unshipped) {
        $existing = Get-Content $unshipped | Where-Object { $_ -notmatch '^\s*$' -and $_ -ne $header }
    }

    # 2. Собираем дельту (новые RS0016 / RS0017)
    $output = & dotnet build $projectPath --no-incremental -v:m -nologo -p:BuildProjectReferences=false 2>&1 | Out-String

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

    # 3. Объединяем дельту с существующим файлом
    $finalSet = New-Object System.Collections.Generic.HashSet[string]
    
    foreach ($line in $existing) {
        # Если символ удален в этом прогоне, не переносим его как активный
        if ($removed -notcontains $line) {
            $null = $finalSet.Add($line)
        }
    }

    foreach ($sym in $added) {
        $null = $finalSet.Add($sym)
    }

    foreach ($sym in $removed) {
        $null = $finalSet.Add("*REMOVED*" + $sym)
    }

    # 4. Записываем объединенный результат обратно
    $sortedLines = $finalSet | Sort-Object
    $content = $header + $newLine
    if ($sortedLines.Count -gt 0) { $content += ($sortedLines -join $newLine) + $newLine }
    
    [System.IO.File]::WriteAllText($unshipped, $content)
    Write-Host ("  added: " + $added.Count + ", removed: " + $removed.Count + ", total lines: " + $sortedLines.Count)
}

Write-Host '=== verify: full rebuild, expect zero RS0016/RS0017 ==='
& dotnet build (Join-Path $repoRoot 'Instancium.Vectra.slnx') -v:m -nologo