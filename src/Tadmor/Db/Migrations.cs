using System.Reflection;
using Npgsql;

namespace Tadmor.Db;

/// <summary>
/// Applies the shared schema in db/migrations (embedded in the assembly) as
/// spec/README.md requires: every *.up.sql in lexical order, each in its own
/// transaction together with the schema_migrations row that records it, so
/// a failed migration leaves no trace. The server runs it at startup, before
/// it accepts requests, as tadmor does.
/// </summary>
internal static class Migrations
{
    private const string Prefix = "migrations/";
    private const string Suffix = ".up.sql";

    /// <summary>Applies the pending migrations and returns the versions newly applied.</summary>
    public static async Task<IReadOnlyList<string>> ApplyAsync(NpgsqlDataSource db, CancellationToken ct = default)
    {
        await using (var cmd = db.CreateCommand("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version    text        PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            )
            """))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var done = new HashSet<string>();
        await using (var cmd = db.CreateCommand("SELECT version FROM schema_migrations"))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                done.Add(reader.GetString(0));
            }
        }

        var assembly = Assembly.GetExecutingAssembly();
        var files = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(Suffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            // A broken build, not an up-to-date schema.
            throw new InvalidOperationException("no *.up.sql migrations are embedded in the assembly");
        }

        var applied = new List<string>();
        foreach (var file in files)
        {
            var version = file[Prefix.Length..^Suffix.Length];
            if (done.Contains(version))
            {
                continue;
            }
            string sql;
            using (var stream = assembly.GetManifestResourceStream(file)!)
            using (var text = new StreamReader(stream))
            {
                sql = await text.ReadToEndAsync(ct);
            }

            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            // No parameters, so nothing in the file is taken for one; Npgsql
            // splits the statements itself, dollar-quoted bodies and all.
            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var cmd = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES ($1)", conn, tx))
            {
                cmd.Parameters.AddWithValue(version);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            applied.Add(version);
        }
        if (applied.Count > 0)
        {
            // Npgsql loaded the database's types when it first connected,
            // which on a fresh database was before citext existed.
            await db.ReloadTypesAsync(ct);
        }
        return applied;
    }
}
