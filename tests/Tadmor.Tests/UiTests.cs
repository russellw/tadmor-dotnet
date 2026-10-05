using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Tadmor.Commands;
using Tadmor.Db;

namespace Tadmor.Tests;

/// <summary>
/// The UI driven over HTTP as a browser drives it: the real server started
/// in-process on TEST_DATABASE_URL (wiped first), cookies kept, forms posted
/// with their anti-forgery tokens, and the pages read back. The line
/// editor's script is not run here; docs/ui-coverage.md says how it was checked.
/// </summary>
[TestClass]
public sealed partial class UiTests
{
    private const string AdminEmail = "admin@ui.test";
    private const string AdminPassword = "admin-password";
    private static WebApplication app = null!;
    private static string baseUrl = "";

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        var url = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Assert.Fail("TEST_DATABASE_URL is required; it names a throwaway database ending in _test");
        }
        await ResetDb.WipeAsync(url);
        app = await Server.BuildAsync(new Config(PostgresUrl.ToConnectionString(url), IPAddress.Loopback, 0), []);
        await app.StartAsync();
        baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        await using var scope = app.Services.CreateAsyncScope();
        await AddUser.UpsertAsync(scope.ServiceProvider.GetRequiredService<TadmorDb>(), AdminEmail, "Ada Admin", AdminPassword, isAdmin: true);
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    /// <summary>A browser: cookies kept, redirects reported rather than followed.</summary>
    private sealed class Browser
    {
        private readonly HttpClient http = new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseUrl),
        };

        public sealed record Page(HttpStatusCode Status, string Html, string? Location, HttpResponseMessage Response);

        public async Task<Page> GetAsync(string path)
        {
            var r = await http.GetAsync(path);
            return new Page(r.StatusCode, await r.Content.ReadAsStringAsync(), r.Headers.Location?.OriginalString, r);
        }

        /// <summary>
        /// Opens the page (or the one named by from), then posts the form with
        /// that page's anti-forgery token.
        /// </summary>
        public async Task<Page> SubmitAsync(string path, IEnumerable<KeyValuePair<string, string>> fields, string? handler = null,
            string? from = null)
        {
            var form = await GetAsync(from ?? path);
            var token = Token().Match(form.Html).Groups[1].Value;
            var body = new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token)));
            var r = await http.PostAsync(handler is null ? path : $"{path}?handler={handler}", body);
            return new Page(r.StatusCode, await r.Content.ReadAsStringAsync(), r.Headers.Location?.OriginalString, r);
        }

        public async Task SignInAsync(string email, string password)
        {
            var r = await SubmitAsync("/login", [new("email", email), new("password", password)]);
            Assert.AreEqual(HttpStatusCode.Redirect, r.Status, r.Html);
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex Token();

    private static async Task<Browser> AdminAsync()
    {
        var b = new Browser();
        await b.SignInAsync(AdminEmail, AdminPassword);
        return b;
    }

    private static KeyValuePair<string, string> F(string name, string value) => new(name, value);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}";

    [TestMethod]
    public async Task WithoutASessionPagesGoToLoginAndTheApiSays401()
    {
        var b = new Browser();
        var page = await b.GetAsync("/sales-invoices");
        Assert.AreEqual(HttpStatusCode.Redirect, page.Status);
        Assert.AreEqual("/login?next=%2Fsales-invoices", page.Location);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/accounts")).Status);
        Assert.AreEqual(HttpStatusCode.OK, (await b.GetAsync("/login")).Status);
        // Assets the login page needs are served without a session.
        Assert.AreEqual("text/css", (await b.GetAsync("/app.css")).Response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task FailedLoginShowsAnErrorAndSigningOutEndsTheSession()
    {
        var b = new Browser();
        var refused = await b.SubmitAsync("/login", [F("email", AdminEmail), F("password", "wrong-password")]);
        Assert.AreEqual(HttpStatusCode.Unauthorized, refused.Status);
        StringAssert.Contains(refused.Html, "class=\"error\"");

        await b.SignInAsync(AdminEmail, AdminPassword);
        var home = await b.GetAsync("/");
        StringAssert.Contains(home.Html, "Ada Admin");
        StringAssert.Contains(home.Html, "Receivables outstanding");

        // Signing out is a form in every page's header.
        var signedOut = await b.SubmitAsync("/login", [], handler: "logout", from: "/");
        Assert.AreEqual("/login", signedOut.Location);
        Assert.AreEqual(HttpStatusCode.Redirect, (await b.GetAsync("/")).Status);
    }

    [TestMethod]
    public async Task AFormCreatesARecordAndShowsARefusalWithWhatWasTyped()
    {
        var b = await AdminAsync();
        var code = Unique("WH");
        var created = await b.SubmitAsync("/warehouses/new", [F("code", code), F("name", "Main")]);
        Assert.AreEqual(HttpStatusCode.Redirect, created.Status, created.Html);
        StringAssert.Matches(created.Location!, new Regex(@"^/warehouses/\d+$"));
        StringAssert.Contains((await b.GetAsync("/warehouses")).Html, code);

        var duplicate = await b.SubmitAsync("/warehouses/new", [F("code", code), F("name", "Second")]);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.Status);
        StringAssert.Contains(duplicate.Html, "class=\"error\"");
        StringAssert.Contains(duplicate.Html, "value=\"Second\"");
    }

    [TestMethod]
    public async Task AnInvoiceIsCreatedPostedUnpostedAndDeleted()
    {
        var b = await AdminAsync();
        Assert.AreEqual(HttpStatusCode.Redirect, (await b.SubmitAsync("/periods/years/new",
            [F("name", Unique("FY")), F("start_date", "2301-01-01"), F("end_date", "2301-12-31")])).Status);
        var org = await b.SubmitAsync("/organizations/new", [F("name", Unique("Customer"))]);
        var orgId = org.Location!.Split('/').Last();
        var revenue = Regex.Match((await b.GetAsync("/accounts")).Html, "href=\"/accounts/(\\d+)\">4000<").Groups[1].Value;
        var receivable = Regex.Match((await b.GetAsync("/accounts")).Html, "href=\"/accounts/(\\d+)\">1100<").Groups[1].Value;
        var customer = await b.SubmitAsync("/customers/new", [F("organization_id", orgId), F("ar_account_id", receivable)]);
        var customerId = customer.Location!.Split('/').Last();

        var number = Unique("INV");
        var saved = await b.SubmitAsync("/sales-invoices/new",
        [
            F("customer_id", customerId), F("invoice_number", number), F("invoice_date", "2301-03-15"), F("currency_code", "USD"),
            F("line_description", "Consulting"), F("line_quantity", "2"), F("line_unit_price", "10.005"), F("line_revenue_account_id", revenue),
            // A row left blank is dropped.
            F("line_description", ""), F("line_quantity", "1"), F("line_unit_price", ""), F("line_revenue_account_id", ""),
        ]);
        Assert.AreEqual(HttpStatusCode.Redirect, saved.Status, saved.Html);
        var invoice = saved.Location!;
        var detail = await b.GetAsync(invoice);
        StringAssert.Contains(detail.Html, number);
        StringAssert.Contains(detail.Html, "USD 20.01");

        Assert.AreEqual(HttpStatusCode.Redirect, (await b.SubmitAsync(invoice, [], "post")).Status);
        detail = await b.GetAsync(invoice);
        StringAssert.Contains(detail.Html, "Posted as journal entry");
        StringAssert.Contains(detail.Html, "href=\"/journal-entries/");

        // A refused action is shown on the page it came from.
        var refused = await b.SubmitAsync(invoice, [], "delete");
        Assert.AreEqual(HttpStatusCode.Conflict, refused.Status);
        StringAssert.Contains(refused.Html, "class=\"error\"");

        Assert.AreEqual(HttpStatusCode.Redirect, (await b.SubmitAsync(invoice, [], "unpost")).Status);
        StringAssert.Contains((await b.GetAsync(invoice)).Html, "Unposted; reversal entry");
        var deleted = await b.SubmitAsync(invoice, [], "delete");
        Assert.AreEqual("/sales-invoices", deleted.Location);
        Assert.AreEqual(HttpStatusCode.NotFound, (await b.GetAsync(invoice)).Status);
    }

    [TestMethod]
    public async Task AnOrdinaryUserSeesNoAdministratorActions()
    {
        var admin = await AdminAsync();
        var email = Unique("user") + "@ui.test";
        Assert.AreEqual(HttpStatusCode.Redirect, (await admin.SubmitAsync("/users/new",
            [F("email", email), F("full_name", "Uma User"), F("password", "user-password")])).Status);

        var user = new Browser();
        await user.SignInAsync(email, "user-password");
        var home = await user.GetAsync("/");
        Assert.IsFalse(home.Html.Contains("href=\"/users\""), "the Users link is shown to an ordinary user");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await user.GetAsync("/users")).Status);
        var settings = await user.GetAsync("/settings");
        Assert.IsFalse(settings.Html.Contains("name=\"base_currency\""), "settings are editable by an ordinary user");
        StringAssert.Contains((await admin.GetAsync("/settings")).Html, "name=\"base_currency\"");
    }

    [TestMethod]
    public async Task AnUnknownAddressShowsNotFound()
    {
        var b = await AdminAsync();
        foreach (var path in new[] { "/no-such-page", "/customers/999999", "/sales-invoices/999999" })
        {
            var page = await b.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.NotFound, page.Status, path);
            StringAssert.Contains(page.Html, "Not found");
        }
    }

    [TestMethod]
    public async Task PagesCarryASameOriginContentSecurityPolicy()
    {
        var page = await (await AdminAsync()).GetAsync("/reports/trial-balance");
        var csp = string.Join("", page.Response.Headers.GetValues("Content-Security-Policy"));
        StringAssert.Contains(csp, "script-src 'self'");
        StringAssert.Contains(csp, "style-src 'self'");
        Assert.IsFalse(page.Html.Contains("style=\""), "a page has an inline style, which the policy would block");
    }

    [TestMethod]
    public async Task AReportExplainsAMalformedDate()
    {
        var page = await (await AdminAsync()).GetAsync("/reports/balance-sheet?as_of=soon");
        Assert.AreEqual(HttpStatusCode.OK, page.Status);
        StringAssert.Contains(page.Html, "soon is not a date");
    }
}
