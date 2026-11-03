Get-ChildItem -Path .\src -Directory | ForEach-Object {
    foreach ($name in 'PublicAPI.Shipped.txt', 'PublicAPI.Unshipped.txt') {
        $path = Join-Path $_.FullName $name
        if (-not (Test-Path $path)) {
            [System.IO.File]::WriteAllText($path, "#nullable enable`r`n")
            Write-Host "created $path"
        }
    }
}