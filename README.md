# Instancium Vectra

**Ultra-fast, zero-dependency, in-process vector database and search engine for .NET 10.**

**Instancium Vectra** is designed to provide high-performance vector storage and semantic search directly inside your .NET applications. It eliminates the operational overhead of running external database clusters, Docker containers, or managing complex C++ P/Invoke binaries across diverse target environments.

Built on an **Open Core** philosophy and distributed under the **Apache-2.0** license, Vectra delivers enterprise-grade deterministic search, memory-mapped disk segments, append-only WAL logging, and managed hardware acceleration (`Vector256` / `Vector512`) right out of the box.

## Why Vectra?

| Characteristic | Traditional Vector DBs (Qdrant, Milvus, Pinecone) | Native C/C++ Libraries (FAISS, Hnswlib) | **Instancium Vectra** |
| :--- | :--- | :--- | :--- |
| **Deployment** | Client-server (Docker, K8s, Cloud SaaS) | In-process, but platform-dependent | **True In-Process (Embedded)** |
| **Setup Overhead** | Ports, network security, cluster management | Cross-compiling, P/Invoke, ABI issues | **1 package: `dotnet add package`** |
| **Dependencies** | gRPC / REST network stacks | Heavy C++ shared runtimes (`.dll`/`.so`) | **0 mandatory dependencies (pure BCL)** |
| **Data Perimeter** | Potential network exposure | Local | **Local & Air-gapped by default** |
| **Target Workloads** | Heavy backend microservices | Research scripts, single-node C++ | **Desktop (MAUI/Lens), Edge, Microservices** |

### The Core Problem Solved

Embedding AI search into desktop apps (such as [Instancium Lens](https://github.com/instancium/lens)), local worker agents, or self-contained microservices should not require spinning up external network services or fighting transient native crash dumps. Vectra embeds seamlessly into any .NET 10 application with zero configuration.

## Key Architectural Principles

1. **Clean Separation of Responsibilities:**
   * **Vectra does not chunk text or parse PDFs.** Upstream pipelines handle data extraction.
   * **Vectra does not mandate an LLM or Embedding model.** Upstream gateways (like [InstantAIGate](https://github.com/instancium/instant-ai-gate)) provide vectors via `IEmbeddingProvider`.
   * **Vectra stores, indexes, and searches vectors deterministically.**

2. **Deterministic Search with Conformance Guards:**
   * Every similarity calculation (`Cosine`, `DotProduct`, `Euclidean`) is verified against a `ReferenceSimilarityKernel` (double-precision accumulation) with a strict relative error bound (`<= 1e-5`).
   * Tie-breaking is deterministic (ordered by `VectorScore` descending, then `VectorId` ascending).

3. **v2 Segmented Storage & WAL:**
   * Immutable disk segments (`.vec`, `.map`, `.pay`) designed for `MemoryMappedFile` zero-copy reads.
   * Append-only Write-Ahead Logging (WAL) ensures atomic mutations without rewriting full indices on updates.

4. **Transparent Scoring:**
   * Raw hardware `VectorScore` is never mutated or obscured.
   * Lexical, cross-encoder, or BM25 reranking is delegated to modular `IReranker` implementations.

## Installation

Add the single consolidated package to your .NET 10 project:

```bash
dotnet add package Instancium.Vectra
```

*(No native drivers, C++ redistributables, or configuration files required).*

## Quickstart

### 1. Vector Search (Core Engine)

```csharp
using Instancium.Vectra;
using Instancium.Vectra.Core.Models;

// 1. Initialize the engine with zero external config
using IVectraEngine engine = VectraEngineFactory.Create(new VectraOptions
{
    BaseDirectory = "./data/vectra"
});

// 2. Open or create a strongly-typed collection
IVectraCollection collection = engine.GetOrOpenCollection("technical-specs", new CollectionOptions(
    Dimension: 768,
    Metric: SimilarityMetric.Cosine,
    ModelId: "qwen3-embedding-0.6b"
));

// 3. Ingest vectors with associated payload text
var sampleVector = new float[768]; // populate from your embedding source
long vectorId = await collection.AddAsync(new VectorEntry(
    VectorId: null, // auto-generated
    Vector: sampleVector,
    Payload: "The differential pressure transmitter measures pressure across two ports."
));

// 4. Perform ultra-fast SIMD search
var results = await collection.SearchAsync(
    new SearchQuery(sampleVector),
    new SearchOptions(TopK: 3)
);

foreach (var hit in results)
{
    Console.WriteLine($"[Hit #{hit.VectorId}] Raw Score: {hit.VectorScore:F4}");
    Console.WriteLine($"Payload: {hit.Payload}");
}
```

### 2. Text Convenience Extensions

When combined with an implementation of `IEmbeddingProvider` (e.g. connected to InstantAIGate or local ONNX runtimes):

```csharp
using Instancium.Vectra.Extensions;

IEmbeddingProvider embedder = GetMyEmbeddingProvider();

// Store text directly (generates embedding + persists payload)
await collection.AddTextAsync(embedder, "Type K thermocouple documentation...", payload: "Full Doc #104");

// Search by raw query string
var hits = await collection.SearchTextAsync(embedder, "thermocouple Seebeck effect", new SearchOptions(TopK: 5));
```

## Ecosystem Integration

Vectra acts as the embedded retrieval engine for the Instancium local-first AI stack:

```text
┌────────────────────────────────────────────────────────┐
│            Instancium Lens (Desktop UI / App)          │
│         [Ingestion, Chunking, Document Workspace]      │
└───────────────┬────────────────────────┬───────────────┘
                │                        │
         Embed / Infer                   │ Vectors & Payloads
                ▼                        ▼
┌───────────────────────────────┐ ┌───────────────────────────────┐
│        InstantAIGate          │ │       Instancium Vectra       │
│  [Local LLM & Embedding GW]   │ │   [Embedded Vector Database]  │
│  (Qwen / Vulkan / llama.cpp)  │ │   (.NET 10 / SIMD / Disk WAL) │
└───────────────────────────────┘ └───────────────────────────────┘
```

## Open Core & Enterprise Scaling

Instancium Vectra is open-source and free for commercial and non-commercial use under the **Apache-2.0** license.

### Managed SIMD vs. Native Enterprise Engine

* **Managed Engine (Default, Open Source):** Employs hardware-accelerated .NET intrinsics (`Vector256<float>` / `Vector512<float>`) and zero-allocation memory spans. Provides sub-millisecond p95 latency for tens of thousands of vectors.

Data is stored as contiguous binary segments optimized for direct memory mapping (mmap). This eliminates deserialization overhead, enables zero-copy reads, and keeps disk files fully portable across different runtimes without migration.

```csharp
options.Kernel = KernelProvider.Native; // Zero schema changes. Zero re-indexing.
```

## Roadmap

* **Graph-Based Indexing (HNSW):** Complement brute-force SIMD search with approximate nearest neighbor graph exploration for multi-million vector collections.
* **Quantization Pipeline:** In-engine scalar (SQ8) and binary quantization for reduced RAM usage.
* **Future Native Acceleration:** Optional C++ native compute backend (`Instancium.Vectra.Native`) for high-throughput enterprise workloads requiring specialized AVX-512/NUMA parallelism, without modifying public APIs or storage formats.

### ⚠️ Project Status & R&D Disclaimer

**Instancium.Vectra** is an active **R&D initiative and core infrastructure component** developed by [Instancium](https://instancium.com), an independent research and development laboratory.

* **Production-Proven Foundation:** VVectra actively powers the local retrieval and knowledge layer across Instancium's own products, including [Instancium Lens](https://github.com/Instancium/Lens).


* **Stabilization Track (1.x):** The current `1.1.x` series serves as an embedded validation track, refined against real-world workloads alongside [InstantAIGate](https://github.com/Instancium/instant-ai-gate).


* **Ecosystem Convergence (2.0):** While the storage engine and execution kernels are production-tested daily, public API surfaces remain subject to clean architectural refinement until the unified **v2.0.0** ecosystem release.


## License

This project is licensed under the [Apache License, Version 2.0](LICENSE.txt).

Copyright (c) 2026 Instancium. All rights reserved.