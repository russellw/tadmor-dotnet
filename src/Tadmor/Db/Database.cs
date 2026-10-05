using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tadmor.Db;

internal static class Database
{
    public static NpgsqlDataSource Open(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    public static DbContextOptions<TadmorDb> ContextOptions(NpgsqlDataSource db) =>
        new DbContextOptionsBuilder<TadmorDb>().UseNpgsql(db).Options;
}
