using Microsoft.EntityFrameworkCore;
using Tadmor.Api;
using Tadmor.Db;
using Tadmor.Services;

namespace Tadmor;

internal static class Server
{
    public static async Task<int> RunAsync(string[] args)
    {
        Config config;
        try
        {
            config = Config.FromEnvironment();
        }
        catch (ConfigException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }

        var builder = WebApplication.CreateBuilder(args);
        // Refusals by the schema become 409s and 422s (Api/Errors), so EF
        // Core need not log each failed command; real faults are logged there.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            if (config.ListenHost is null)
            {
                k.ListenAnyIP(config.ListenPort);
            }
            else
            {
                k.Listen(config.ListenHost, config.ListenPort);
            }
        });

        var db = Database.Open(config.ConnectionString);
        builder.Services.AddSingleton(db);
        builder.Services.AddDbContext<TadmorDb>(o => o.UseNpgsql(db));
        builder.Services.ConfigureHttpJsonOptions(o => Json.Configure(o.SerializerOptions));
        AddServices(builder.Services);

        var app = builder.Build();

        var applied = await Migrations.ApplyAsync(db);
        if (applied.Count > 0)
        {
            app.Logger.LogInformation("applied migrations {Versions}", string.Join(", ", applied));
        }

        app.UseMiddleware<Errors>();
        app.Use(AuthApi.Middleware);
        Health.Map(app);
        var api = app.MapGroup("/api");
        AuthApi.Map(api);
        MasterApi.Map(api);
        DocumentsApi.Map(api);
        OrdersApi.Map(api);
        LedgerApi.Map(api);
        // Unknown paths, and known paths with an unknown method, are a JSON 404.
        app.Map("/api/{**rest}", () => Http.Error(404, "not found"));

        await app.RunAsync();
        return 0;
    }

    /// <summary>The services, one per request, shared by the API and the UI.</summary>
    internal static void AddServices(IServiceCollection services)
    {
        services.AddScoped<Auth>();
        services.AddScoped<Users>();
        services.AddScoped<Organizations>();
        services.AddScoped<Parties>();
        services.AddScoped<Products>();
        services.AddScoped<Accounts>();
        services.AddScoped<Catalog>();
        services.AddScoped<Calendar>();
        services.AddScoped<Ledger>();
        DocumentsApi.AddServices(services);
        OrdersApi.AddServices(services);
        LedgerApi.AddServices(services);
    }
}
