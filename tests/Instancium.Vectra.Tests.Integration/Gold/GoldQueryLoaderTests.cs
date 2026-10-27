// tests/Instancium.Vectra.Tests.Integration/Gold/GoldQueryLoaderTests.cs
#nullable enable
namespace Instancium.Vectra.Tests.Integration.Gold;

using System.IO;
using System.Text.Json;
using Instancium.Vectra.Core.Models;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GoldQueryLoaderTests
{
    [Fact]
    public void GoldQueryLoader_ShouldDeserializeAllQueries()
    {
        IReadOnlyList<GoldQuery> queries = GoldQueryLoader.Load();

        Assert.NotEmpty(queries);
        Assert.All(queries, q => Assert.False(string.IsNullOrWhiteSpace(q.Query)));
        Assert.All(queries, q => Assert.True(q.ExpectedPageFrom > 0));
        Assert.All(queries, q => Assert.True(q.ExpectedPageTo >= q.ExpectedPageFrom));
    }

    [Fact]
    public void GoldQueryLoader_ShouldThrowWhenFileMissing()
    {
        Assert.Throws<FileNotFoundException>(() => GoldQueryLoader.Load("gold/missing.json"));
    }

    [Fact]
    public void GoldQueryLoader_ShouldThrowOnInvalidPageBounds()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, "[{\"query\":\"q\",\"expectedPageFrom\":5,\"expectedPageTo\":2}]");
        try
        {
            Assert.Throws<InvalidOperationException>(() => GoldQueryLoader.Load(Path.GetFullPath(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}