using Npgsql;

namespace Tadmor.Services;

/// <summary>
/// Maps a refusal by the shared schema to the status the spec gives it, by
/// SQLSTATE, as tadmor does: a unique violation is a 409 (a duplicate key),
/// and a foreign-key, check, not-null, or exclusion violation, an exception
/// raised by a trigger, or a data exception (a value out of range) is a 422.
/// Anything else is a server fault.
/// </summary>
public static class DatabaseErrors
{
    public static ServiceException? Refusal(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
        {
            if (x is PostgresException pg)
            {
                int? status = pg.SqlState switch
                {
                    "23505" => 409,
                    "23503" or "23514" or "23502" or "23P01" or "P0001" => 422,
                    var s when s.StartsWith("22", StringComparison.Ordinal) => 422,
                    _ => null,
                };
                return status is null ? null : new ServiceException(status.Value, pg.MessageText);
            }
        }
        return null;
    }
}
