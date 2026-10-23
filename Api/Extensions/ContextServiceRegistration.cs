using AlfaHackaton.DapperContext;
using AlfaHackaton.HttpClient;
using ContextService.Migrations;
using ContextService.Repositories;
using ContextService.Repositories.Interfaces;
using ContextService.Services;
using ContextService.Services.Interfaces;
using ContextService.Settings;
using ContextService.Workers;
using Microsoft.AspNetCore.Mvc;

namespace ContextService.Extensions;

public static class ContextServiceRegistration
{
    public static IServiceCollection AddContextServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must identify the PostgreSQL context database.");
        }

        var database = new DapperSettings { ConnectionString = connectionString };
        services.AddDapper(database);
        services.AddMigrations(database, typeof(InitialContext).Assembly);
        services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .ConfigureApiBehaviorOptions(options =>
            {
                options.SuppressMapClientErrors = true;
                options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new
                {
                    code = "invalid_request",
                    message = "The request body or parameters are invalid."
                });
            });
        services.AddSingleton<CompanyEvidenceRepository>();
        services.AddSingleton<IMaterialRepository, PostgresMaterialRepository>();
        services.AddSingleton(sp => new MaterialService(sp.GetRequiredService<IMaterialRepository>(),
            configuration.GetValue<int?>("Context:MaxCharacters") ?? 120000));
        services.AddSingleton<IMaterialService>(sp => sp.GetRequiredService<MaterialService>());
        services.AddAlfaHttpClient(new CompanyHttpSettings
        {
            BaseAddress = string.IsNullOrWhiteSpace(configuration["CompanyService:BaseUrl"]) ? "" : RetrievalClient.SafeUri(configuration["CompanyService:BaseUrl"]!.TrimEnd('/') + "/").ToString(),
            Timeout = TimeSpan.FromSeconds(15),
            AutoRedirect = false,
            DefaultHeaders = headers => headers.Add("X-Internal-Secret", configuration["CompanyService:InternalSecret"] ?? configuration["Internal:Secret"] ?? "")
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        });
        services.AddSingleton<RetrievalTransport<CompanyHttpSettings>>();
        services.AddSingleton<CompanyKnowledgeClient>();
        services.AddSingleton<ContextChanges>();
        services.AddSingleton<ContextChangeService>();
        var qdrant = new QdrantHttpSettings
        {
            BaseAddress = RetrievalClient.SafeUri((configuration["Qdrant:BaseUrl"] ?? "http://127.0.0.1:6333").TrimEnd('/') + "/").ToString(),
            Timeout = TimeSpan.FromSeconds(30),
            AutoRedirect = false,
            DefaultHeaders = headers =>
            {
                if (!string.IsNullOrWhiteSpace(configuration["Qdrant:ApiKey"]))
                {
                    headers.Add("api-key", configuration["Qdrant:ApiKey"]);
                }
            }
        };
        services.AddAlfaHttpClient(qdrant);
        services.AddAlfaHttpClient(new InferenceHttpSettings { Timeout = TimeSpan.FromSeconds(60), AutoRedirect = false });
        services.AddSingleton<RetrievalTransport<QdrantHttpSettings>>();
        services.AddSingleton<RetrievalTransport<InferenceHttpSettings>>();
        services.AddSingleton<RetrievalClient>();
        services.AddSingleton<SearchProjectionRepository>();
        services.AddSingleton<SearchProjectionService>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<RetrievedContextService>();
        if (configuration.GetValue<bool>("Qdrant:WorkerEnabled"))
        {
            services.AddHostedService<SearchProjectionWorker>();
        }

        return services;
    }
}
