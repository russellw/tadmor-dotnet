using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tadmor.Api;
using Tadmor.Services;

namespace Tadmor.Ui;

/// <summary>
/// The base of every signed-in page. A refusal by a service is shown on the
/// page, next to the action that caused it (spec/domain.md §13 G5); a record
/// that does not exist is the not-found page (G8).
/// </summary>
public abstract class UiPage : PageModel
{
    public CurrentUser Me => HttpContext.User();

    /// <summary>The server's message for a refused action, shown on the page.</summary>
    public string? Error { get; set; }

    /// <summary>A one-off message carried across a redirect.</summary>
    [TempData]
    public string? Notice { get; set; }

    /// <summary>The posted form, as the API would read it.</summary>
    protected Input Form(params string[] arrays) => FormInput.From(Request.Form, arrays);

    /// <summary>
    /// Runs an action. A refusal becomes Error, and the page is rendered
    /// again by onRefusal, which reloads what the page shows.
    /// </summary>
    protected async Task<IActionResult> Attempt(Func<Task<IActionResult>> action, Func<Task<IActionResult>> onRefusal)
    {
        try
        {
            return await action();
        }
        catch (Exception e) when ((e as ServiceException ?? DatabaseErrors.Refusal(e)) is { Status: not 404 } refusal)
        {
            Error = refusal.Message;
            Response.StatusCode = refusal.Status;
            return await onRefusal();
        }
    }

    /// <summary>403 unless the user is an administrator (the server enforces what the UI hides).</summary>
    protected void RequireAdmin()
    {
        if (!Me.IsAdmin)
        {
            throw ServiceException.Forbidden();
        }
    }

    /// <summary>A path id, or the not-found page when it is malformed.</summary>
    protected static int PathId(string text) =>
        int.TryParse(text, out var id) && id > 0 ? id : throw ServiceException.NotFound();
}
