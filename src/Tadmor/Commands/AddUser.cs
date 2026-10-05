using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Commands;

/// <summary>
/// The out-of-band bootstrap of a login (spec/api.md §3), as tadmor's
/// <c>server -adduser</c>: creates the user, or when the email exists resets
/// its name, password, and admin flag and reactivates it. The password is
/// the first line of stdin. The user is an administrator unless
/// --admin=false. Pending migrations are applied first, so it works on a
/// fresh database.
/// <code>
/// echo 'the-password' | Tadmor adduser --email=you@example.com --name='Your Name'
/// </code>
/// </summary>
internal static class AddUser
{
    private const string Usage = "usage: adduser --email=EMAIL --name=NAME [--admin=false] < password";

    public static async Task<int> RunAsync(string[] args)
    {
        string? email = null, name = null;
        var admin = true;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--email=", StringComparison.Ordinal))
            {
                email = arg["--email=".Length..].Trim();
            }
            else if (arg.StartsWith("--name=", StringComparison.Ordinal))
            {
                name = arg["--name=".Length..].Trim();
            }
            else if (arg is "--admin=false")
            {
                admin = false;
            }
            else if (arg is not "--admin=true")
            {
                return Fail($"unknown argument {arg}\n{Usage}");
            }
        }
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(name))
        {
            return Fail($"adduser requires --email and --name\n{Usage}");
        }
        if (!email.Contains('@'))
        {
            return Fail("email must contain @");
        }
        var password = Console.In.ReadLine();
        if (password is null || password.Length < 8)
        {
            return Fail("password (the first line of stdin) must be at least 8 characters");
        }

        Config config;
        try
        {
            config = Config.FromEnvironment();
        }
        catch (ConfigException e)
        {
            return Fail(e.Message);
        }
        await using var db = Database.Open(config.ConnectionString);
        await Migrations.ApplyAsync(db);
        await using var context = new TadmorDb(Database.ContextOptions(db));
        var id = await UpsertAsync(context, email, name, password, admin);
        Console.WriteLine($"user {id} {email}{(admin ? " (administrator)" : "")}");
        return 0;
    }

    /// <summary>Creates or resets the user and returns its id.</summary>
    internal static async Task<int> UpsertAsync(TadmorDb context, string email, string fullName, string password, bool isAdmin)
    {
        var hash = new PasswordHasher<User>().HashPassword(null!, password);
        // An INSERT cannot be wrapped in a subquery, so the result is
        // materialized as it comes rather than composed with Single().
        var ids = await context.Database.SqlQuery<int>($"""
            INSERT INTO users (email, full_name, password_hash, is_admin) VALUES ({email}, {fullName}, {hash}, {isAdmin})
            ON CONFLICT (email) DO UPDATE
            SET full_name = EXCLUDED.full_name, password_hash = EXCLUDED.password_hash,
                is_active = true, is_admin = EXCLUDED.is_admin
            RETURNING id
            """).ToListAsync();
        return ids.Single();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
