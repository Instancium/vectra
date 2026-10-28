param(
    [switch]$SkipAutomatedTests
)

$ErrorActionPreference = "Stop"

# --- Environment -------------------------------------------------------------
# VECTRA_REQUIRE_GATEWAY=1: xUnit не самопроверяется; тесты идут и падают громко,
# если шлюз реально недоступен. Решение о skip принимает этот скрипт по /health/live.
$env:VECTRA_EMBEDDING_GATEWAY_URL = "http://localhost:5001"
$env:VECTRA_CHAT_GATEWAY_URL = "http://localhost:5000"
$env:VECTRA_REQUIRE_GATEWAY = "1"
if (-not $env:VECTRA_GATEWAY_API_KEY) {
    $env:VECTRA_GATEWAY_API_KEY = "ChangeMeSuperSecureAdminApiKey123"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location (Join-Path $scriptDir "..\..")
Write-Host "Working directory: $(Get-Location)" -ForegroundColor Gray

# --- Health probes (guide v1.4: /health/live always 200; /health/ready 200 vs 503) ---
function Test-GatewayLive([string]$url) {
    try {
        $response = Invoke-WebRequest -Uri "$url/health/live" -TimeoutSec 3 -UseBasicParsing
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Test-GatewayReady([string]$url) {
    try {
        $response = Invoke-WebRequest -Uri "$url/health/ready" -TimeoutSec 5 -UseBasicParsing
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

$embeddingLive = Test-GatewayLive $env:VECTRA_EMBEDDING_GATEWAY_URL
$embeddingReady = $embeddingLive -and (Test-GatewayReady $env:VECTRA_EMBEDDING_GATEWAY_URL)
$chatLive = Test-GatewayLive $env:VECTRA_CHAT_GATEWAY_URL
$chatReady = $chatLive -and (Test-GatewayReady $env:VECTRA_CHAT_GATEWAY_URL)

Write-Host ("Embedding gateway (5001): live={0} ready={1}" -f $embeddingLive, $embeddingReady) -ForegroundColor $(if ($embeddingReady) { 'Green' } else { 'Yellow' })
Write-Host ("Chat gateway     (5000): live={0} ready={1}" -f $chatLive, $chatReady) -ForegroundColor $(if ($chatReady) { 'Green' } else { 'Yellow' })

if ($embeddingLive -and -not $embeddingReady) {
    Write-Warning "Embedding gateway alive but model not Ready: integration tests will fail loudly until loading completes."
}
if ($chatLive -and -not $chatReady) {
    Write-Warning "Chat gateway alive but model not Ready: 'ask' will report 'not ready'; chat integration test will SKIP."
}

# --- [1/5] Build ---------------------------------------------------------------
Write-Host "`n=== [1/5] Build solution ===" -ForegroundColor Cyan
dotnet build Instancium.Vectra.slnx -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipAutomatedTests) {
    # --- [2/5] Unit -------------------------------------------------------------
    Write-Host "`n=== [2/5] Unit tests (no gateway, no latency budgets) ===" -ForegroundColor Cyan
    dotnet test tests/Instancium.Vectra.Tests.Unit --filter "Category=Unit" --no-build --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # --- [3/5] Bench + Arch -----------------------------------------------------
    Write-Host "`n=== [3/5] In-memory scale bench + arch tests ===" -ForegroundColor Cyan
    dotnet test tests/Instancium.Vectra.Tests.Unit --filter "Category=Bench" --no-build --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test tests/Instancium.Vectra.Tests.Arch --filter "Category=Arch" --no-build --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # --- [4/5] Integration ------------------------------------------------------
    if ($embeddingLive) {
        Write-Host "`n=== [4/5] Integration tests (live embedding gateway; REQUIRE=1 -> no xUnit self-skip) ===" -ForegroundColor Cyan
        dotnet test tests/Instancium.Vectra.Tests.Integration --filter "Category=Integration" --no-build --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } else {
        Write-Host "`n=== [4/5] Integration tests SKIPPED: nothing listens on $env:VECTRA_EMBEDDING_GATEWAY_URL (/health/live) ===" -ForegroundColor Yellow
    }
} else {
    Write-Host "`n=== [2/5..4/5] Automated tests skipped by -SkipAutomatedTests ===" -ForegroundColor Yellow
}

# --- [5/5] RAG REPL e2e ----------------------------------------------------------
if (-not $embeddingLive) {
    Write-Host "`n=== [5/5] REPL e2e SKIPPED: embedding gateway down; start gateways to demo index/search/ask ===" -ForegroundColor Yellow
    Write-Host "`n=== Flow complete (offline mode: build + unit + bench + arch) ===" -ForegroundColor Green
    exit 0
}

if ($chatLive) {
    Write-Host "`n=== [5/5a] Chat wire-contract probe (guide v1.4 handshake) ===" -ForegroundColor Cyan
    dotnet run --project src/Instancium.Vectra.Cli --no-build -- probe-chat
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "probe-chat reported failures above; 'ask' will fail until wire names match the deployed hub."
    }
} else {
    Write-Warning "Chat gateway down: REPL 'ask' will print an error inside the session (session survives by design)."
}

Write-Host "`n=== [5/5b] REPL e2e: index -> stats -> search -> ask (streamed) ===" -ForegroundColor Cyan
$replSession = @(
    'index "tests\assets\pdf\test-doc.pdf" 1 20',
    'stats',
    'search "pressure transmitter diaphragm"',
    'ask "How does a pressure transmitter convert diaphragm deformation into an electrical signal?" 3',
    'exit'
) -join [Environment]::NewLine

$replSession | dotnet run --project src/Instancium.Vectra.Cli --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== RAG test flow complete ===" -ForegroundColor Green