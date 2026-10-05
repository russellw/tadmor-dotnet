using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tadmor.Commands;
using Tadmor.Db;

namespace Tadmor.Tests;

/// <summary>
/// Integration tests against TEST_DATABASE_URL, whose public schema is
/// wiped first. Its name must end in _test (ResetDb refuses anything else).
/// </summary>
[TestClass]
public sealed class DatabaseTests
{
    private static NpgsqlDataSource db = null!;
    private static IReadOnlyList<string> firstRun = [];

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        var url = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Assert.Fail("TEST_DATABASE_URL is required; it names a throwaway database ending in _test");
        }
        await ResetDb.WipeAsync(url);
        db = Database.Open(PostgresUrl.ToConnectionString(url));
        firstRun = await Migrations.ApplyAsync(db);
    }

    [ClassCleanup]
    public static async Task Cleanup() => await db.DisposeAsync();

    [TestMethod]
    public async Task MigrationsApplyEveryFileOnceInOrder()
    {
        var embedded = typeof(Migrations).Assembly.GetManifestResourceNames()
            .Count(n => n.StartsWith("migrations/", StringComparison.Ordinal));
        Assert.IsGreaterThan(0, embedded);
        Assert.HasCount(embedded, firstRun);
        CollectionAssert.AreEqual(firstRun.Order(StringComparer.Ordinal).ToList(), firstRun.ToList());
        Assert.IsEmpty(await Migrations.ApplyAsync(db));
    }

    [TestMethod]
    public async Task AddUserCreatesThenResets()
    {
        await using var context = new TadmorDb(Database.ContextOptions(db));
        var id = await AddUser.UpsertAsync(context, "reset@example.test", "First", "password-one", isAdmin: true);
        await context.Database.ExecuteSqlAsync($"UPDATE users SET is_active = false WHERE id = {id}");

        // The email is citext, so a change of case is the same login.
        var again = await AddUser.UpsertAsync(context, "Reset@Example.test", "Second", "password-two", isAdmin: false);
        Assert.AreEqual(id, again);

        // A variable, so that EF Core sends a parameter rather than a literal.
        var shouted = "RESET@EXAMPLE.TEST";
        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Email == shouted);
        Assert.AreEqual(id, user.Id);
        Assert.AreEqual("Second", user.FullName);
        Assert.IsTrue(user.IsActive);
        Assert.IsFalse(user.IsAdmin);
        Assert.AreEqual(PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, "password-two"));
    }

    [TestMethod]
    public async Task ResetDbRefusesOrdinaryDatabases()
    {
        await Assert.ThrowsExactlyAsync<ConfigException>(
            () => ResetDb.WipeAsync("postgres://tadmor:tadmor@127.0.0.1:5432/tadmor_dotnet"));
    }
}
