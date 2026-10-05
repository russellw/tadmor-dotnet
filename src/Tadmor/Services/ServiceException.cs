namespace Tadmor.Services;

/// <summary>
/// A refusal with the status the spec assigns it (spec/api.md §1.4).
/// Services throw it; its message is shown to the user by the JSON API and
/// the UI alike.
/// </summary>
public sealed class ServiceException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;

    /// <summary>400: a required field is missing, or the request cannot be interpreted.</summary>
    public static ServiceException BadRequest(string message) => new(400, message);

    public static ServiceException Unauthorized(string message) => new(401, message);

    public static ServiceException Forbidden() => new(403, "administrator only");

    public static ServiceException NotFound() => new(404, "not found");

    /// <summary>409: a duplicate, or the record is in the wrong state for the operation.</summary>
    public static ServiceException Conflict(string message) => new(409, message);

    /// <summary>422: a value is unacceptable, or a business rule refuses the request.</summary>
    public static ServiceException Unprocessable(string message) => new(422, message);
}
