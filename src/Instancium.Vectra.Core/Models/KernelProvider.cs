using System;

namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Defines the execution environment for similarity and search kernels.
/// Resolves the architectural requirement to support Native Acceleration (P/Invoke)
/// alongside the default Managed SIMD implementations.
/// </summary>
[Flags]
public enum KernelProvider
{
    Managed = 1,
    Native = 2
}
