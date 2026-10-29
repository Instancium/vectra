# Instancium.Vectra

**High-Performance In-Memory Vector Search & RAG Pipeline for .NET 10**

Instancium.Vectra is a self-contained semantic search and RAG engine that runs entirely within a .NET process. It ingests PDF documents, generates embeddings via [InstantAIGate](https://github.com/Instancium/instant-ai-gate) inference gateways, stores vectors in memory with SIMD-accelerated brute-force search, and produces grounded answers through a chat gateway — no external vector database required.

---

## Current Status: MVP

The project validates the core hypothesis:

> *An agent takes a PDF, quickly chunks and embeds it, holds vectors in memory, and delivers fast, accurate retrieval-augmented answers.*

### Measured Success Criteria

| Metric | Target |
|---|---|
| Retrieval accuracy (Top-3 hit-rate, gold query set) | ≥ 80 % |
| Search latency (p95, in-process, brute-force SIMD) | < 20 ms |
| 20-page end-to-end indexing (incl. live embeddings) | Completes without errors |
| Determinism (same query → same results) | Guaranteed |

### Explicitly Out of Scope (YAGNI)

Disk persistence (`vectors.bin` / `manifest.json`), incremental re-indexing, SHA-256 deduplication, Worker mode, HNSW / IVF indexes, P/Invoke native acceleration, model hot-swap. These belong to post-MVP evolution (see [Concept](#long-term-vision)).

---

## Architecture

```
┌──────────────────────────────────────────────────────────-───┐
│                     Instancium.Vectra.Cli                    │
│              REPL · one-shot commands · bench                │
└──────┬──────────┬──────────┬─────────-─┬──────────┬──────────┘
       │          │          │           │          │
┌──────▼─-─┐ ┌────▼────┐ ┌───▼───-─┐ ┌───▼───┐ ┌─-──▼──----─--─┐
│ Indexing │ │ Search  │ │ Storage │ │  Rag  │ │ Infrastructure│
│ Pipeline │ │ Engine  │ │ Store   │ │Service│ │               │
└──────┬──-┘ └────┬────┘ └───┬───-─┘ └───┬───┘ └───┬───-------─┘
       │          │          │           │         │
┌──────▼────────-─▼──────────▼────────-──▼─────────▼─-─────────┐
│                    Instancium.Vectra.Core                    │
│                  Contracts · Models · Exceptions             │
└───────────────────────────────────────────────────────-──────┘
```

### Module Responsibilities

| Project | Responsibility |
|---|---|
| **Core** | Contracts (`IVectorStore`, `IVectorSearchEngine`, `IIndexingPipeline`, `IRagService`, `ITextExtractor`, `ITextChunker`, `IChatCompletionClient`), domain models, exceptions. Zero external dependencies. |
| **Indexing** | PDF text extraction (PdfPig), page-aware fixed-size chunking, orchestration pipeline. Depends on `InstantAIGate.Core` contracts only. |
| **Infrastructure** | SignalR adapters for embedding and chat gateways. The **only** layer that knows about SignalR transport. Error mapping, retry with backoff, token budget validation. |
| **Storage** | `InMemoryVectorStore` — flat vector array + chunk metadata. Thread-safe upsert, span-based access. |
| **Search** | `InMemoryVectorSearchEngine` — brute-force dot product with `System.Numerics.Vector<T>` SIMD, Top-K via `PriorityQueue`. |
| **Rag** | `RagService` — query embedding → vector search → lexical re-ranking → prompt construction → streamed chat answer. `RagPromptBuilder` enforces citation and refusal contracts. |
| **Cli** | DI composition, REPL loop, one-shot commands, benchmark runner. |

### Architectural Invariants (enforced by `ArchitectureTests`)

- **Core** references no other Vectra layer and no `InstantAIGate` types.
- **Indexing** never references **Infrastructure** or SignalR.
- **Storage** and **Search** reference only **Core**.
- **SignalR** is known exclusively to **Infrastructure**.
- **Rag** never references **Infrastructure**, **Indexing**, or SignalR.

---

## Pipeline Flow

```
PDF file
  │
  ▼
PdfTextExtractor          (PdfPig, page range [from..to])
  │  IReadOnlyList<PageText>
  ▼
PageAwareFixedSizeChunker (1200 chars, 200 overlap, no cross-page splits)
  │  IReadOnlyList<TextChunk>
  ▼
InstantAIGateEmbeddingAdapter  (SignalR → embedding gateway :5001)
  │  Parallel.ForEachAsync, MaxParallelism = 4
  │  retry on 429, context overflow guard
  ▼
InMemoryVectorStore.Upsert()
  │
  ▼
InMemoryVectorSearchEngine.SearchAsync()   (SIMD dot product, Top-K)
  │
  ▼
RagService.RetrieveAsync()   (lexical IDF re-rank boost)
  │
  ▼
RagPromptBuilder.Build()     (numbered citations, refusal fallback)
  │
  ▼
InstantAIGateChatAdapter.StreamAnswerAsync()  (SignalR → chat gateway :5000)
  │  IAsyncEnumerable<string>
  ▼
Console / caller
```

---

## Quick Start

### Prerequisites

1. **Embedding gateway** running on `http://localhost:5001` with model `qwen3-embedding-0.6b` loaded and `Embeddings: true` in its hardware profile.
2. **Chat gateway** running on `http://localhost:5000` with a chat model loaded (e.g. `qwen3-4b-instruct`). Required only for `ask` command.
3. API key with `GatewayUser` role.

### Environment Variables

| Variable | Purpose | Default |
|---|---|---|
| `VECTRA_GATEWAY_API_KEY` | Bearer token for both gateways | **required** |
| `VECTRA_EMBEDDING_GATEWAY_URL` | Embedding gateway base URL | `http://localhost:5001` |
| `VECTRA_CHAT_GATEWAY_URL` | Chat gateway base URL | `http://localhost:5000` |

### Build & Run

```bash
dotnet build Instancium.Vectra.slnx

# Interactive REPL (index persists in process memory across commands)
dotnet run --project src/Instancium.Vectra.Cli

# One-shot indexing
dotnet run --project src/Instancium.Vectra.Cli -- index ./corpus/book.pdf 1 20

# One-shot search (requires REPL for index persistence)
dotnet run --project src/Instancium.Vectra.Cli -- search "temperature measurement" 3
```

### CLI Commands

| Command | Description |
|---|---|
| `index <pdf> <from> <to>` | Extract pages, chunk, embed, store in memory |
| `search "<query>" [topK]` | Vector search with timing breakdown |
| `ask "<question>" [topK]` | Retrieve Top-K context + stream grounded answer from chat gateway |
| `stats` | Vector count, dimension, page range, file count |
| `bench <pdf> [iterations]` | Run 20 → 50 → 100 page benchmark matrix, write `artifacts/bench-report.md` |
| `probe-chat` | Validate chat gateway wire contract (guide v1.4 handshake) |
| `help` | Print usage |
| `exit` / `quit` | Stop REPL |

> **Note:** In one-shot mode the index is discarded when the process exits. Use the REPL to keep the index across multiple commands.

---

## Configuration

Configuration is loaded from `config_vectra.json` (and optional `config_vectra.local.json` override) in the output directory.

```json
{
  "Vectra": {
    "Gateways": {
      "Embedding": {
        "Url": "http://localhost:5001",
        "HubPath": "/hub/gateway",
        "RepoId": "qwen3-embedding-0.6b",
        "MaxParallelRequests": 4,
        "MaxRetryAttempts": 3,
        "ConnectionTimeout": "00:00:30"
      },
      "Chat": {
        "Url": "http://localhost:5000",
        "HubPath": "/hub/gateway",
        "RepoId": "qwen3-vl-8b-instruct"
      }
    },
    "Indexing": {
      "ChunkSizeChars": 1200,
      "ChunkOverlapChars": 200
    },
    "Cli": {
      "DefaultTopK": 3,
      "DefaultBenchIterations": 5,
      "BenchPageWindows": [20, 50, 100]
    }
  }
}
```

API keys are resolved from environment variables first, then from configuration sections. Never commit `config_vectra.local.json`.

---

## InstantAIGate Integration

Vectra consumes the **existing** `InstantAIGate.Core` NuGet package (pinned at `1.3.1`) as the single source of truth for the wire contract. No mirror DTOs, no custom interfaces.

### Contract Surface Used

| InstantAIGate.Core Type | Usage |
|---|---|
| `IInferenceEngine` | Implemented by `InstantAIGateEmbeddingAdapter` (embedding-only subset) |
| `EmbeddingRequest` / `EmbeddingResponse` | Canonical embedding DTOs |
| `ChatMessage` / `SessionTokenDelta` | Chat streaming DTOs |
| `GatewayStatusDetails` / `GatewayOperationalStatus` | Readiness probing |
| `NativeModelDetails` | Dimension and context size introspection |
| `ContextOverflowException` / `QueueFullException` | Error mapping targets |

### Wire Protocol (per client-integration-guide v1.4)

**Embedding flow:**
1. `GetGatewayStatus` → assert `Ready`
2. `GetEmbeddingDimensionAsync(repoId)` → validate dimension
3. `GenerateEmbeddingsAsync(repoId, request)` → parallel batch with backoff

**Chat flow:**
1. `GetGatewayStatus` → assert `Ready`
2. `JoinSession(sessionId, repoId)`
3. `SendPromptDelta(sessionId, message)` → stream `ReceiveTokenDelta`
4. `DestroySession(sessionId)` in `finally`

### Error Handling

| Gateway Condition | Vectra Behavior |
|---|---|
| `QueueFullException` (429) | Exponential backoff retry (up to `MaxRetryAttempts`) |
| `ContextOverflowException` | `VectraException` with chunk-size reduction guidance |
| Model draining (503) | `VectraException` with retry-later message |
| Gateway not `Ready` | `VectraException` before any indexing/chat attempt |
| `TokenCount > ContextSize` | Client-side rejection in adapter |

---

## Testing

### Test Projects

| Project | Scope |
|---|---|
| `Tests.Unit` | Chunking invariants, store upsert/replace, search correctness, adapter error mapping, RAG prompt construction, pipeline orchestration (mocked contracts) |
| `Tests.Integration` | Live gateway embedding generation, 20-page pipeline e2e, gold-query retrieval, chat RAG streaming, CLI e2e with stub engine |
| `Tests.Arch` | Reflection-based layer dependency enforcement |

### Running Tests

```bash
# Unit + Arch (no gateway required)
dotnet test tests/Instancium.Vectra.Tests.Unit
dotnet test tests/Instancium.Vectra.Tests.Arch

# Integration (skips gracefully if gateway is offline)
dotnet test tests/Instancium.Vectra.Tests.Integration

# Force integration tests to fail instead of skip
VECTRA_REQUIRE_GATEWAY=1 dotnet test tests/Instancium.Vectra.Tests.Integration
```

### Gold Query Validation

The bench suite (`IndexingPipelineBenchTests`) indexes 100 pages of the test corpus and validates:

- **Top-3 hit-rate ≥ 0.8** against `tests/assets/gold/queries.json` (15 queries with known page ranges)
- **p95 search latency ≤ 20 ms** (in-process SIMD search only)
- **Determinism:** repeated searches return identical chunk IDs and scores

Results are written to `artifacts/bench-report.md`.

---

## Benchmarking

```bash
dotnet run --project src/Instancium.Vectra.Cli -- bench ./corpus/book.pdf 7
```

The bench command:
1. Probes total page count via the extractor
2. For each window (20 → 50 → 100 pages): indexes, embeds 5 fixed queries, runs N+1 search rounds
3. Reports: index time, chunks/sec, search p95
4. Writes markdown report to `artifacts/bench-report.md`

---

## Tech Stack

| Component | Technology |
|---|---|
| Runtime | .NET 10, C# latest, `#nullable enable` |
| PDF Extraction | PdfPig (UglyToad.PdfPig 0.1.16) |
| Vector Math | `System.Numerics.Vector<T>` (SIMD) |
| Transport | SignalR Client 10.0.14 (Infrastructure layer only) |
| Contracts | InstantAIGate.Core 1.3.1 (NuGet) |
| DI | Microsoft.Extensions.DependencyInjection |
| Configuration | Microsoft.Extensions.Configuration (JSON + env) |
| Testing | xUnit 2.9.3, coverlet |
| Architecture Tests | Reflection-based assembly reference validation |

---

## Solution Layout

```
Instancium.Vectra.slnx
├── src/
│   ├── Instancium.Vectra.Core            # Contracts, models, exceptions
│   ├── Instancium.Vectra.Indexing        # PDF extraction, chunking, pipeline
│   ├── Instancium.Vectra.Infrastructure  # SignalR adapters (embedding + chat)
│   ├── Instancium.Vectra.Storage         # In-memory vector store
│   ├── Instancium.Vectra.Search          # SIMD brute-force search engine
│   ├── Instancium.Vectra.Rag             # RAG service, prompt builder
│   └── Instancium.Vectra.Cli             # REPL + one-shot CLI
├── tests/
│   ├── Instancium.Vectra.Tests.Unit
│   ├── Instancium.Vectra.Tests.Integration
│   ├── Instancium.Vectra.Tests.Arch
│   ├── assets/pdf/                       # sample-2pages.pdf, test-doc.pdf
│   └── assets/gold/queries.json
├── artifacts/                            # bench-report.md (generated)
├── config_vectra.json
└── README.md
```

---

## License

Apache License 2.0

Part of the **Instancium Ecosystem**.