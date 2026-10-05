using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tadmor.Api;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages;

/// <summary>The login screen (spec/domain.md §13 G1), and signing out (G2).</summary>
public sealed class LoginModel(Auth auth) : PageModel
{
    public string? Error { get; private set; }
    public string? Email { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Next { get; set; }

    public IActionResult OnGet() => HttpContext.Items.ContainsKey("tadmor.user") ? Redirect("/") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        Email = Request.Form["email"];
        try
        {
            var (_, token) = await auth.LoginAsync(FormInput.From(Request.Form));
            AuthApi.SetCookie(HttpContext, token);
            // Only a local path, so the login screen cannot be used to redirect elsewhere.
            return LocalRedirect(Next is { Length: > 1 } n && n.StartsWith('/') && !n.StartsWith("//") ? n : "/");
        }
        catch (ServiceException e)
        {
            Error = e.Status == 400 ? "Enter your email and password." : "That email and password do not match an active login.";
            Response.StatusCode = e.Status;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await auth.LogoutAsync(Request.Cookies[AuthApi.Cookie]);
        AuthApi.ClearCookie(HttpContext);
        return Redirect("/login");
    }
}
