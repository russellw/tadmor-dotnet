using Microsoft.AspNetCore.Mvc;
using Tadmor.Services;
using Tadmor.Ui;

namespace Tadmor.Pages.Master;

/// <summary>
/// A master-data form, for creating ("new") or editing a record. A refused
/// save shows the server's message above the form, keeping what was typed
/// (spec/domain.md §13 G5). Records are deactivated here, never deleted (M6).
/// </summary>
public sealed class EditModel(Lookups lookups) : UiPage
{
    public Resource Resource { get; private set; } = null!;
    public string Key { get; private set; } = "";
    public bool IsNew => Key == "new";
    public string Heading { get; private set; } = "";
    public List<Field> Fields { get; private set; } = [];
    public Dictionary<string, List<Option>> Choices { get; } = [];
    private Dictionary<string, object?> record = [];

    private async Task LoadAsync(string resource, string key)
    {
        Resource = Resource.Find(resource) ?? throw ServiceException.NotFound();
        if (Resource.AdminOnly)
        {
            RequireAdmin();
        }
        Key = key;
        Fields = Resource.Fields.Where(f => IsNew ? !f.EditOnly : !f.CreateOnly).ToList();
        if (!IsNew)
        {
            record = Resource.Row(await Resource.Get(HttpContext.RequestServices, key));
            Heading = Format.Raw(record.GetValueOrDefault(Resource.Fields[0].Name));
            if (Resource.Path is "customers" or "suppliers")
            {
                Heading = (await lookups.OrganizationNamesAsync()).GetValueOrDefault(Format.Int(record["organization_id"]) ?? 0, Heading);
            }
        }
        foreach (var f in Fields.Where(f => f.Options is not null))
        {
            // The account form's parent picker is told which account it is editing.
            var current = f.Name == "parent_id" ? (IsNew ? null : key) : Value(f.Name);
            Choices[f.Name] = await f.Options!(lookups, current);
            if (f.Name == "parent_id" && Value(f.Name) is { Length: > 0 } parent && Choices[f.Name].All(o => o.Value != parent))
            {
                Choices[f.Name].Insert(0, new Option(parent, parent + " (inactive)"));
            }
        }
    }

    /// <summary>A field's value: as posted when re-showing a refused form, else as stored.</summary>
    public string Value(string name)
    {
        if (Request.HasFormContentType)
        {
            return Request.Form[name].ToString();
        }
        return Format.Raw(record.GetValueOrDefault(name));
    }

    public Task OnGetAsync(string resource, string key) => LoadAsync(resource, key);

    public async Task<IActionResult> OnPostAsync(string resource, string key)
    {
        await LoadAsync(resource, key);
        return await Attempt(async () =>
        {
            var sp = HttpContext.RequestServices;
            if (IsNew)
            {
                var created = await Resource.Create(sp, Form());
                Notice = $"Created {Resource.Singular}.";
                return Redirect($"/{Resource.Path}/{Uri.EscapeDataString(created)}");
            }
            await Resource.Update(sp, key, Form(), Me);
            Notice = "Saved.";
            return Redirect($"/{Resource.Path}/{Uri.EscapeDataString(key)}");
        }, async () =>
        {
            await LoadAsync(resource, key);
            return Page();
        });
    }
}
