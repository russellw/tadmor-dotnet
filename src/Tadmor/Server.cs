using Microsoft.EntityFrameworkCore;
using Tadmor.Api;
using Tadmor.Db;

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

        var builder = WebApplication.CreateSlimBuilder(args);
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

        var app = builder.Build();

        var applied = await Migrations.ApplyAsync(db);
        if (applied.Count > 0)
        {
            app.Logger.LogInformation("applied migrations {Versions}", string.Join(", ", applied));
        }

        Health.Map(app);
        await app.RunAsync();
        return 0;
    }
}
