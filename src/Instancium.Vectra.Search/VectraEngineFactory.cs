#nullable enable
namespace Instancium.Vectra.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage.Disk;
using System;

/// <summary>
/// Static entry point for creating a standalone in-process vector engine without DI container overhead.
/// </summary>
public static class VectraEngineFactory
{
    public static IVectraEngine Create(VectraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resolver = new InstanciumDirectoryResolver();
        var reader = new DiskIndexReader();
        var writer = new DiskIndexWriter();

        return new VectraEngine(options, resolver, reader, writer);
    }
}