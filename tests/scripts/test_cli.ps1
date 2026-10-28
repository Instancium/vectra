$env:VECTRA_EMBEDDING_GATEWAY_URL = "http://localhost:5001"
$env:VECTRA_GATEWAY_API_KEY = "ChangeMeSuperSecureAdminApiKey123"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Join-Path $scriptDir "..\.."
Set-Location $solutionRoot

$cliProject = "src\Instancium.Vectra.Cli\Instancium.Vectra.Cli.csproj"
$testPdf = "tests\assets\pdf\test-doc.pdf"

Write-Host "=== [1/2] REPL session: index -> stats -> search (single process) ===" -ForegroundColor Cyan
$replScript = @(
    "index `"$testPdf`" 1 2",
    "stats",
    "search `"alpha beta`"",
    "exit"
) -join [Environment]::NewLine

$replScript | dotnet run --project $cliProject
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== [2/2] Bench matrix 20/50/100 (one-shot) ===" -ForegroundColor Cyan
dotnet run --project $cliProject --no-build -- bench $testPdf 3
exit $LASTEXITCODE