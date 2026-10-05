using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Tadmor.Db;

/// <summary>
/// The EF Core view of the shared schema. Entities are written by hand
/// (docs/stack.md) and map the tables as they are; EF Core never creates or
/// migrates anything.
/// </summary>
internal sealed class TadmorDb(DbContextOptions<TadmorDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(u => u.Id).ValueGeneratedOnAdd();
            // Mapped as citext so that parameters compared with it are sent
            // as citext; as text, the comparison would be case-sensitive.
            e.Property(u => u.Email).HasColumnType("citext");
            e.Property(u => u.CreatedAt).ValueGeneratedOnAdd();
            e.Property(u => u.UpdatedAt).ValueGeneratedOnAddOrUpdate();
        });

        // The schema's names are snake_case; the entities' are PascalCase.
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(SnakeCase(property.Name));
            }
        }
    }

    internal static string SnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}

internal sealed class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
