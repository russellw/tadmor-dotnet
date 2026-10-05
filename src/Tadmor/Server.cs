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
        var app = await BuildAsync(config, args);
        await app.RunAsync();
        return 0;
    }

    /// <summary>
    /// The configured application, with pending migrations applied, ready
    /// to start. The tests start it in-process to drive the UI over HTTP.
    /// </summary>
    internal static async Task<WebApplication> BuildAsync(Config config, string[] args)
    {
        // The content root is the executable's directory, where wwwroot is
        // published, whatever the working directory; the application is this
        // assembly, whichever one started it.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args, ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(Server).Assembly.GetName().Name,
        });
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
        builder.Services.AddRazorPages(o =>
        {
            // "new" forms share their page with editing.
            o.Conventions.AddPageRoute("/Docs/Edit", "{kind:regex(^(sales-invoices|purchase-bills|sales-credit-notes|purchase-credit-notes)$)}/new");
            o.Conventions.AddPageRoute("/Payments/Edit", "{kind:regex(^(customer-payments|supplier-payments)$)}/new");
            o.Conventions.AddPageRoute("/Orders/Edit", "{kind:regex(^(sales-orders|purchase-orders)$)}/new");
            o.Conventions.AddPageRoute("/Stock/Edit", "stock-movements/new");
            o.Conventions.AddPageRoute("/Bank/Edit", "bank-statements/new");
        }).AddApplicationPart(typeof(Server).Assembly);
        builder.Services.AddScoped<Ui.Lookups>();
        builder.Services.AddScoped<Ui.Dashboard>();

        var app = builder.Build();

        var applied = await Migrations.ApplyAsync(db);
        if (applied.Count > 0)
        {
            app.Logger.LogInformation("applied migrations {Versions}", string.Join(", ", applied));
        }

        app.UseMiddleware<Errors>();
        // An unknown UI address is the not-found page (spec/domain.md §13 G8).
        app.UseStatusCodePagesWithReExecute("/error", "?status={0}");
        app.Use(SecurityHeaders);
        app.UseStaticFiles();
        app.Use(AuthApi.Middleware);
        app.UseRouting();
        Health.Map(app);
        var api = app.MapGroup("/api");
        AuthApi.Map(api);
        MasterApi.Map(api);
        DocumentsApi.Map(api);
        OrdersApi.Map(api);
        LedgerApi.Map(api);
        // Unknown paths, and known paths with an unknown method, are a JSON 404.
        app.Map("/api/{**rest}", () => Http.Error(404, "not found"));
        app.MapRazorPages();
        return app;
    }

    /// <summary>
    /// A same-origin Content Security Policy on every page: the UI's one
    /// script and one stylesheet are served from here, and nothing is inline.
    /// </summary>
    private static Task SecurityHeaders(HttpContext ctx, RequestDelegate next)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; "
                + "form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'";
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        return next(ctx);
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
