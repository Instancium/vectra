# Instancium.Vectra

**High-Performance In-Memory Vector Search Engine for .NET 10**

Instancium.Vectra is a high-performance semantic search engine designed to run directly within a .NET application process. It eliminates the need for external vector databases (such as Qdrant or Milvus), providing minimal latency, predictable performance, and simplified deployment.

## 🎯 Key Features

*   **In-Process Execution:** Vector search is performed in-memory without network calls (HTTP/gRPC).
*   **Lazy Loading:** Text chunks are loaded from disk only after search results (Top-K) are retrieved, significantly reducing RAM usage.
*   **Incremental Indexing:** Supports incremental index updates based on SHA-256 file hashes. Only changed data is re-indexed.
*   **Architecture Evolution:** The abstract search core allows seamless transition from pure C# implementation (.NET Intrinsics/SIMD) to hybrid mode with native acceleration (P/Invoke, AVX2/AVX512) without breaking public API contracts.
*   **Ecosystem Integration:** Fully compatible with **InstantAIGate** via the stable IEmbeddingGenerationService contract.

## 🏗 Architecture

The project is divided into two independent subsystems:

### 1. Vectra Search Core
Responsible exclusively for working with the ready-made index.
*   Storage of vectors and metadata.
*   Execution of vector search (Cosine Similarity / Dot Product).
*   Lazy loading of texts from chunks.bin.
*   **Independent** of the file system, InstantAIGate, or embedding generation processes.

### 2. Indexing Pipeline
Responsible for data preparation and index construction.
*   Directory scanning and file hash calculation.
*   Text extraction and chunking.
*   Embedding generation via the InstantAIGate.Core adapter.
*   Management of indexing state (file.state, chunk.map).

## 🚀 Usage Modes

The single core supports three launch scenarios:

1.  **Library Mode:** Integration into ASP.NET Core or Desktop applications as a NuGet package.
2.  **CLI Tool:** Console utility for manual index building (vectra build ./docs) and searching.
3.  **Worker Service:** Background service for periodic change scanning and automatic re-indexing.

## 📦 Index Structure

The index is a self-contained artifact consisting of the following files:

    index/
    ├── manifest.json   # Metadata: version, model, dimensions, statistics
    ├── file.state      # File state: paths, hashes, processing status
    ├── chunk.map       # Mapping: Chunk ID -> File ID, offset, metadata
    ├── vectors.bin     # Binary vector storage (float32/quantized)
    ├── offsets.bin     # Text chunk offsets
    └── chunks.bin      # Source texts (read on demand)

## 🔌 Integration with InstantAIGate

Vectra has no direct dependencies on SignalR or internal InstantAIGate types. Interaction is handled through the IEmbeddingGenerationService interface from the InstantAIGate.Core package.

**Requirements for InstantAIGate:**
*   Model must be loaded with the Embeddings: true flag.
*   Access to the GenerateEmbeddingsAsync method via SignalR Hub or direct service call.

## 🛠 Tech Stack

*   **.NET 10** (C# latest features, #nullable enable)
*   **Clean Architecture / DDD**
*   **System.Runtime.Intrinsics** (AVX2/AVX-512 ready)
*   **MemoryMappedFile / Span<T>** for efficient memory management
*   **SignalR Client** (only in the InstantAIGate adapter layer)

## 📄 License

[Specify License Type, e.g., Apache License 2.0]

---
*Part of the Instancium Ecosystem.*