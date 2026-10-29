namespace Instancium.Vectra.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;

public static class VectraConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddVectraConfiguration(this IConfigurationBuilder builder, string fileName = "config_vectra.json")
    {
        ArgumentNullException.ThrowIfNull(builder);

        string configFileName = string.IsNullOrWhiteSpace(fileName) ? "config_vectra.json" : fileName;
        string localFileName = Path.ChangeExtension(configFileName, ".local.json");

        builder
            .AddJsonFile(configFileName, optional: true, reloadOnChange: false)
            .AddJsonFile(localFileName, optional: true, reloadOnChange: false);

        return builder;
    }
}
