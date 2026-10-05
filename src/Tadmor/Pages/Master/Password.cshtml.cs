using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Master;

/// <summary>An administrator's password reset for a user (spec/domain.md §13 M7).</summary>
public sealed class PasswordModel(Users users) : UiPage
{
    public int Id { get; private set; }
    public string Email { get; private set; } = "";

    private async Task<IActionResult> LoadAsync(int id)
    {
        RequireAdmin();
        Id = id;
        Email = (await users.GetAsync(id)).Email;
        return Page();
    }

    public Task<IActionResult> OnGetAsync(int id) => LoadAsync(id);

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await LoadAsync(id);
        return await Attempt(async () =>
        {
            await users.ResetPasswordAsync(id, Form());
            Notice = "Password reset; the user's sessions were signed out.";
            return Redirect($"/users/{id}");
        }, () => LoadAsync(id));
    }
}
