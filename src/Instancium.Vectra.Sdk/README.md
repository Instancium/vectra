
# Instancium.Vectra.Sdk

[![License: Apache-2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Target Framework](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

`Instancium.Vectra.Sdk` is the official client SDK and high-level orchestration engine for the **Instancium.Vectra** vector database and document processing pipeline. It provides a unified, profile-isolated facade (`IVectraEngine`) for scanning, chunking, embedding, persisting, and querying document corpora with low latency and minimal RAM overhead.

---

## Key Features

- **Unified Facade (`IVectraEngine`):** Single entry point handling indexing, semantic search, and index lifecycle telemetry.
- **Profile Isolation:** Dedicated on-disk binary stores located at `.instancium/vectra/{profileId}/index`. Multiple profiles do not share or corrupt each other's vector space.
- **Incremental Indexing:** Built-in SHA-256 change classification avoids redundant re-vectorization of unchanged files.
- **Automatic Snippet Hydration:** Searches read vector matches directly from memory and automatically hydrate UTF-8 text payloads from disk (`chunks.bin`).
- **Hardware Acceleration:** Hardware-accelerated vector SIMD calculations with fallback for dot product similarity queries.
- **InstantAIGate Integration:** Seamless SignalR transport to local AI gateways for embedding inference with resilient exponential backoff and token budget guardrails.

---

## Installation

Add the package via .NET CLI:

```bash
dotnet add package Instancium.Vectra.Sdk

```

Or via Package Manager Console:

```powershell
Install-Package Instancium.Vectra.Sdk

```

---

## Configuration

Add the Vectra configuration sections to your `appsettings.json` or `config_vectra.json`:

```json
{
  "Vectra": {
    "Sdk": {
      "BaseStorageDirectory": "",
      "DefaultEmbeddingRepoId": "qwen3-embedding-0.6b",
      "EcosystemFolderName": ".instancium",
      "ProductFolderName": "vectra"
    },
    "Gateways": {
      "Embedding": {
        "Url": "http://localhost:5000",
        "HubPath": "/hub/gateway",
        "RepoId": "qwen3-embedding-0.6b",
        "MaxParallelRequests": 4,
        "ApiKey": "",
        "ConnectionTimeout": "00:00:30",
        "MaxRetryAttempts": 3
      }
    }
  }
}

```

---

## Dependency Injection Setup

Register Vectra Engine in your `IServiceCollection`:

```csharp
using Instancium.Vectra.Sdk.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Register Vectra Engine and its entire underlying subsystem graph
builder.Services.AddVectraEngine(builder.Configuration, options =>
{
    // Optional: override base storage directory programmatically
    options.BaseStorageDirectory = AppContext.BaseDirectory;
});

```

---

## Quick Start

### 1. Indexing a Folder

```csharp
using Instancium.Vectra.Sdk.Contracts;
using Instancium.Vectra.Sdk.Models;

public class LibraryService
{
    private readonly IVectraEngine _vectraEngine;

    public LibraryService(IVectraEngine vectraEngine)
    {
        _vectraEngine = vectraEngine;
    }

    public async Task ProcessDocumentsAsync(string profileId, string folderPath, CancellationToken ct)
    {
        IndexResult result = await _vectraEngine.IndexFolderAsync(profileId, folderPath, ct);

        Console.WriteLine($"Profile: {result.ProfileId}");
        Console.WriteLine($"Indexed files: {result.FilesIndexed}");
        Console.WriteLine($"Chunks added: {result.ChunksAdded}");
        Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F2}s");
    }
}

```

### 2. Semantic Search with Hydrated Snippets

```csharp
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Sdk.Contracts;

public class QueryService
{
    private readonly IVectraEngine _vectraEngine;

    public QueryService(IVectraEngine vectraEngine)
    {
        _vectraEngine = vectraEngine;
    }

    public async Task SearchAsync(string profileId, string userPrompt, CancellationToken ct)
    {
        IReadOnlyList<SearchResult> results = await _vectraEngine.SearchAsync(
            profileId: profileId,
            query: userPrompt,
            topK: 3,
            cancellationToken: ct);

        foreach (var match in results)
        {
            Console.WriteLine($"Score: {match.Score:F4} | Page: {match.PageNumber}");
            Console.WriteLine($"Content: {match.Snippet}");
            Console.WriteLine(new string('-', 40));
        }
    }
}

```

### 3. Checking Profile Index Health

```csharp
IndexStatus status = await _vectraEngine.GetStatusAsync("default-profile", cancellationToken);

if (status.Exists)
{
    Console.WriteLine($"Files: {status.FileCount}, Chunks: {status.ChunkCount}, Dim: {status.Dimension}");
}
else
{
    Console.WriteLine("Index has not been built yet.");
}

```

---

## Third-Party Libraries and Notices

This library relies on third-party open-source components, including `PdfPig`, `InstantAIGate.Core`, and `Microsoft.AspNetCore.SignalR.Client`. For full details regarding licenses, copyrights, and dependencies, please refer to:

- [THIRD-PARTY-NOTICES.md](https://github.com/Instancium/vectra/blob/main/THIRD-PARTY-NOTICES.md)

---

## License

Copyright (c) 2026 Instancium™ (https://instancium.com). All rights reserved.

`Instancium.Vectra.Sdk` is distributed under the **Apache License 2.0**. See the [LICENSE](https://github.com/Instancium/vectra/blob/main/LICENSE.txt) file for details.