using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddVectraInfrastructure(builder.Configuration);
builder.Services.Configure<Instancium.Vectra.Infrastructure.Configuration.EmbeddingGatewayOptions>(
    builder.Configuration.GetSection("Vectra:Gateways:Embedding"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();