using Tadmor.Db;

namespace Tadmor.Commands;

/// <summary>
/// Drops and recreates the public schema of DATABASE_URL, for the tests and
/// the conformance run, which need a fresh instance. Refuses any database
/// whose name does not end in _test or _conformance, so it cannot wipe real
/// data by mistake.
/// <code>
/// DATABASE_URL=postgres://.../tadmor_dotnet_conformance Tadmor resetdb
/// </code>
/// </summary>
internal static class ResetDb
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("usage: resetdb (takes DATABASE_URL from the environment)");
            return 2;
        }
        try
        {
            var url = Environment.GetEnvironmentVariable("DATABASE_URL");
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ConfigException("DATABASE_URL is required");
            }
            var name = await WipeAsync(url);
            Console.WriteLine($"wiped {name}");
            return 0;
        }
        catch (ConfigException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    /// <summary>Wipes the database DATABASE_URL names and returns its name.</summary>
    internal static async Task<string> WipeAsync(string url)
    {
        var name = PostgresUrl.DatabaseName(url);
        if (!name.EndsWith("_test", StringComparison.Ordinal) && !name.EndsWith("_conformance", StringComparison.Ordinal))
        {
            throw new ConfigException($"refusing to wipe database {name}: its name must end in _test or _conformance");
        }
        await using var db = Database.Open(PostgresUrl.ToConnectionString(url));
        await using var cmd = db.CreateCommand("DROP SCHEMA public CASCADE; CREATE SCHEMA public");
        await cmd.ExecuteNonQueryAsync();
        return name;
    }
}
