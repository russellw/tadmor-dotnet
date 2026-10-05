using Npgsql;
using Tadmor.Db;

namespace Tadmor.Tests;

[TestClass]
public sealed class PostgresUrlTests
{
    [TestMethod]
    public void ParsesEveryPart()
    {
        var b = PostgresUrl.Parse("postgres://tad%40mor:p%3Ass@db.example:6543/tadmor_x?sslmode=require&application_name=t");
        Assert.AreEqual("db.example", b.Host);
        Assert.AreEqual(6543, b.Port);
        Assert.AreEqual("tadmor_x", b.Database);
        Assert.AreEqual("tad@mor", b.Username);
        Assert.AreEqual("p:ss", b.Password);
        Assert.AreEqual(SslMode.Require, b.SslMode);
        Assert.AreEqual("t", b.ApplicationName);
    }

    [TestMethod]
    public void DefaultsThePortAndRunsSessionsInUtc()
    {
        var b = PostgresUrl.Parse("postgresql://u@localhost/db");
        Assert.AreEqual(5432, b.Port);
        Assert.AreEqual("UTC", b.Timezone);
        Assert.IsNull(b.Password);
        Assert.AreEqual(GssEncryptionMode.Disable, b.GssEncryptionMode);
        Assert.AreEqual(GssEncryptionMode.Prefer, PostgresUrl.Parse("postgres://u@h/db?gssencmode=prefer").GssEncryptionMode);
    }

    [TestMethod]
    public void RefusesWhatItCannotHonour()
    {
        Assert.ThrowsExactly<ConfigException>(() => PostgresUrl.Parse("Host=localhost;Database=db"));
        Assert.ThrowsExactly<ConfigException>(() => PostgresUrl.Parse("mysql://u@h/db"));
        Assert.ThrowsExactly<ConfigException>(() => PostgresUrl.Parse("postgres://u@h/"));
        Assert.ThrowsExactly<ConfigException>(() => PostgresUrl.Parse("postgres://u@h/db?sslmode=sometimes"));
        Assert.ThrowsExactly<ConfigException>(() => PostgresUrl.Parse("postgres://u@h/db?connect_timeout=5"));
    }
}
