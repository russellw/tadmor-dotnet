using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tadmor.Db;

namespace Tadmor.Services;

/// <summary>The signed-in user, re-read on every request (spec/api.md §3).</summary>
public sealed record CurrentUser(int Id, string Email, string FullName, bool IsAdmin);

/// <summary>
/// Logins and server-side sessions over the shared users and sessions
/// tables (spec/api.md §3). The cookie carries a random token; the table
/// holds only its SHA-256, so a database dump cannot be replayed. A session
/// lasts a fixed 30 days and ends at logout, on deactivation, or when an
/// administrator resets the password.
/// </summary>
public sealed class Auth(TadmorDb db)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private static readonly PasswordHasher<User> Hasher = new();

    // Verified when the email is unknown, so that every failed login does
    // the same work and takes the same time.
    private static readonly string DummyHash = Hasher.HashPassword(null!, "not a password");

    public static string HashPassword(string password) => Hasher.HashPassword(null!, password);

    /// <summary>Checks the credentials and opens a session; returns the user and the cookie token.</summary>
    public async Task<(CurrentUser User, string Token)> LoginAsync(Input input)
    {
        var email = input.Str("email");
        var password = input.Str("password");
        if (email is null || password is null)
        {
            throw ServiceException.BadRequest("email and password are required");
        }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
        var ok = Hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? DummyHash, password)
            != PasswordVerificationResult.Failed;
        if (user is null || !ok || !user.IsActive)
        {
            throw ServiceException.Unauthorized("invalid email or password");
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTimeOffset.UtcNow;
        await db.Sessions.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync();
        db.Sessions.Add(new Session { TokenHash = Hash(token), UserId = user.Id, ExpiresAt = now + Lifetime });
        await db.SaveChangesAsync();
        return (new CurrentUser(user.Id, user.Email, user.FullName, user.IsAdmin), token);
    }

    /// <summary>The live session's user, or null.</summary>
    public async Task<CurrentUser?> LookupAsync(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        var hash = Hash(token);
        var now = DateTimeOffset.UtcNow;
        return await (from s in db.Sessions
                      join u in db.Users on s.UserId equals u.Id
                      where s.TokenHash == hash && s.ExpiresAt > now && u.IsActive
                      select new CurrentUser(u.Id, u.Email, u.FullName, u.IsAdmin)).SingleOrDefaultAsync();
    }

    public async Task LogoutAsync(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            var hash = Hash(token);
            await db.Sessions.Where(s => s.TokenHash == hash).ExecuteDeleteAsync();
        }
    }

    private static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
}

/// <summary>Login users, administered by administrators (spec/api.md §5.1).</summary>
public sealed class Users(TadmorDb db)
{
    public sealed record UserRecord(int Id, string Email, string FullName, bool IsActive, bool IsAdmin);

    private static readonly Expression<Func<User, UserRecord>> Records =
        u => new UserRecord(u.Id, u.Email, u.FullName, u.IsActive, u.IsAdmin);

    public Task<List<UserRecord>> ListAsync() => db.Users.OrderBy(u => u.Email).ThenBy(u => u.Id).Select(Records).ToListAsync();

    public async Task<UserRecord> GetAsync(int id) =>
        await db.Users.Where(u => u.Id == id).Select(Records).SingleOrDefaultAsync() ?? throw ServiceException.NotFound();

    public async Task<int> CreateAsync(Input input)
    {
        var email = Email(input);
        var name = input.ReqStr("full_name");
        var password = Password(input);
        var user = new User
        {
            Email = email, FullName = name, PasswordHash = Auth.HashPassword(password), IsAdmin = input.Bool("is_admin"),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>A full replacement. Administrators may not deactivate or demote themselves.</summary>
    public async Task UpdateAsync(int id, Input input, CurrentUser caller)
    {
        var email = Email(input);
        var name = input.ReqStr("full_name");
        var active = input.Bool("is_active");
        var admin = input.Bool("is_admin");
        var user = await db.Users.FindAsync(id) ?? throw ServiceException.NotFound();
        if (id == caller.Id && (!active || !admin))
        {
            throw ServiceException.Unprocessable("you cannot deactivate or demote yourself");
        }
        user.Email = email;
        user.FullName = name;
        user.IsActive = active;
        user.IsAdmin = admin;
        await db.SaveChangesAsync();
        if (!active)
        {
            await db.Sessions.Where(s => s.UserId == id).ExecuteDeleteAsync();
        }
    }

    /// <summary>Sets a new password and revokes all of the user's sessions.</summary>
    public async Task ResetPasswordAsync(int id, Input input)
    {
        var password = Password(input);
        var user = await db.Users.FindAsync(id) ?? throw ServiceException.NotFound();
        user.PasswordHash = Auth.HashPassword(password);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await db.Sessions.Where(s => s.UserId == id).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }

    private static string Email(Input input)
    {
        var email = input.ReqStr("email");
        return email.Contains('@') ? email : throw ServiceException.Unprocessable("email must contain @");
    }

    private static string Password(Input input)
    {
        var password = input.Str("password") ?? throw ServiceException.BadRequest("password is required");
        return password.Length >= 8 ? password : throw ServiceException.Unprocessable("password must be at least 8 characters");
    }
}
