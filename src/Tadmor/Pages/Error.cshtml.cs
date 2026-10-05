using Microsoft.AspNetCore.Mvc.RazorPages;
using Tadmor.Api;
using Tadmor.Services;

namespace Tadmor.Pages;

/// <summary>Errors and unknown addresses (spec/domain.md §13 G8), shown with their status.</summary>
public sealed class ErrorModel : PageModel
{
    public string Title { get; private set; } = "Error";
    public string Message { get; private set; } = "";

    public void OnGet(int? status)
    {
        var refusal = HttpContext.Items[Errors.ErrorKey] as ServiceException;
        var code = refusal?.Status ?? status ?? Response.StatusCode;
        (Title, Message) = code switch
        {
            404 => ("Not found", "There is nothing at this address. It may have been deleted, or the link may be wrong."),
            403 => ("Not allowed", "Only an administrator can do that."),
            _ when refusal is not null && code < 500 => ("Refused", refusal.Message),
            _ => ("Something went wrong", "The server could not complete the request."),
        };
        if (code >= 400)
        {
            Response.StatusCode = code;
        }
    }
}
