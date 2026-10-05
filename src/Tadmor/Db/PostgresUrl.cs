using Npgsql;

namespace Tadmor.Db;

/// <summary>
/// Turns DATABASE_URL, a libpq-style URL such as
/// postgres://user:pass@host:5432/db?sslmode=disable, into an Npgsql
/// connection string, which Npgsql does not parse itself. Every session
/// runs in UTC (spec/README.md, "The shared schema"). GSS encryption is off
/// unless gssencmode asks for it, so Npgsql does not try to load Kerberos.
/// </summary>
internal static class PostgresUrl
{
    public static string ToConnectionString(string url) => Parse(url).ConnectionString;

    public static NpgsqlConnectionStringBuilder Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql"))
        {
            throw new ConfigException("DATABASE_URL must be a postgres:// URL");
        }
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Timezone = "UTC",
            GssEncryptionMode = GssEncryptionMode.Disable,
        };
        if (uri.UserInfo.Length > 0)
        {
            var parts = uri.UserInfo.Split(':', 2);
            b.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2)
            {
                b.Password = Uri.UnescapeDataString(parts[1]);
            }
        }
        if (string.IsNullOrEmpty(b.Database))
        {
            throw new ConfigException("DATABASE_URL must name a database");
        }
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var value = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
            switch (key)
            {
                case "sslmode":
                    b.SslMode = value switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "require" => SslMode.Require,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => throw new ConfigException($"DATABASE_URL: unknown sslmode {value}"),
                    };
                    break;
                case "gssencmode":
                    b.GssEncryptionMode = value switch
                    {
                        "disable" => GssEncryptionMode.Disable,
                        "prefer" => GssEncryptionMode.Prefer,
                        "require" => GssEncryptionMode.Require,
                        _ => throw new ConfigException($"DATABASE_URL: unknown gssencmode {value}"),
                    };
                    break;
                case "application_name":
                    b.ApplicationName = value;
                    break;
                default:
                    throw new ConfigException($"DATABASE_URL: unsupported parameter {key}");
            }
        }
        return b;
    }

    /// <summary>The database name in DATABASE_URL.</summary>
    public static string DatabaseName(string url) => Parse(url).Database!;
}
