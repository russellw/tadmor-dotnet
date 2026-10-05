using System.Net;

namespace Tadmor;

/// <summary>
/// Settings from the environment, named as tadmor names them (README):
/// DATABASE_URL (required), HTTP_ADDR (host:port, default ":8080", every
/// interface), and PORT, which overrides the port when set, as Cloud Run
/// injects it.
/// </summary>
internal sealed record Config(string ConnectionString, IPAddress? ListenHost, int ListenPort)
{
    public static Config FromEnvironment()
    {
        var url = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ConfigException("DATABASE_URL is required");
        }
        var (host, port) = ParseListen(
            Environment.GetEnvironmentVariable("HTTP_ADDR"), Environment.GetEnvironmentVariable("PORT"));
        return new Config(Db.PostgresUrl.ToConnectionString(url), host, port);
    }

    /// <summary>
    /// Parses HTTP_ADDR and PORT. A null host means every interface. Names
    /// are not resolved, except "localhost", which is the loopback address.
    /// </summary>
    internal static (IPAddress? Host, int Port) ParseListen(string? httpAddr, string? port)
    {
        var addr = string.IsNullOrWhiteSpace(httpAddr) ? ":8080" : httpAddr.Trim();
        var colon = addr.LastIndexOf(':');
        if (colon < 0)
        {
            throw new ConfigException($"HTTP_ADDR must be host:port, got {addr}");
        }
        var hostPart = addr[..colon].Trim('[', ']');
        IPAddress? host = hostPart switch
        {
            "" => null,
            "localhost" => IPAddress.Loopback,
            _ when IPAddress.TryParse(hostPart, out var ip) => ip,
            _ => throw new ConfigException($"HTTP_ADDR host must be an IP address or localhost, got {hostPart}"),
        };
        var portPart = string.IsNullOrWhiteSpace(port) ? addr[(colon + 1)..] : port.Trim();
        if (!int.TryParse(portPart, out var number) || number is < 0 or > 65535)
        {
            throw new ConfigException($"invalid listen port {portPart}");
        }
        return (host, number);
    }
}

/// <summary>A setting is missing or malformed; the message says which.</summary>
internal sealed class ConfigException(string message) : Exception(message);
