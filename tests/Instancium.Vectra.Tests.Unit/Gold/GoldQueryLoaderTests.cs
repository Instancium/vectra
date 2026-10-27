namespace Instancium.Vectra.Tests.Unit.Gold;

using Instancium.Vectra.Tests.Integration.Gold;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GoldQueryLoaderTests
{
    [Fact]
    public void GoldQueryLoader_ShouldDeserializeAllQueries()
    {
        var queries = GoldQueryLoader.Load("gold/queries.json");

        Assert.NotEmpty(queries);
        Assert.All(queries, q => Assert.False(string.IsNullOrWhiteSpace(q.Query)));
        Assert.All(queries, q => Assert.True(q.ExpectedPageFrom > 0));
        Assert.All(queries, q => Assert.True(q.ExpectedPageTo >= q.ExpectedPageFrom));
    }
}
