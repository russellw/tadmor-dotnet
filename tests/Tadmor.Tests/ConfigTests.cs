using System.Net;

namespace Tadmor.Tests;

[TestClass]
public sealed class ConfigTests
{
    [TestMethod]
    public void ListenDefaultsToEveryInterfaceOn8080()
    {
        Assert.AreEqual((null, 8080), Config.ParseListen(null, null));
        Assert.AreEqual((null, 8080), Config.ParseListen(" ", ""));
    }

    [TestMethod]
    public void ListenParsesHostAndPort()
    {
        Assert.AreEqual((IPAddress.Parse("127.0.0.1"), 8090), Config.ParseListen("127.0.0.1:8090", null));
        Assert.AreEqual((IPAddress.Loopback, 80), Config.ParseListen("localhost:80", null));
        Assert.AreEqual((IPAddress.IPv6Loopback, 9000), Config.ParseListen("[::1]:9000", null));
        Assert.AreEqual((null, 3000), Config.ParseListen(":3000", null));
    }

    [TestMethod]
    public void PortOverridesTheListenPort()
    {
        Assert.AreEqual((IPAddress.Parse("127.0.0.1"), 9999), Config.ParseListen("127.0.0.1:8090", "9999"));
        Assert.AreEqual((null, 9999), Config.ParseListen(null, "9999"));
    }

    [TestMethod]
    public void MalformedListenIsRefused()
    {
        Assert.ThrowsExactly<ConfigException>(() => Config.ParseListen("8080", null));
        Assert.ThrowsExactly<ConfigException>(() => Config.ParseListen("example.com:80", null));
        Assert.ThrowsExactly<ConfigException>(() => Config.ParseListen(":http", null));
        Assert.ThrowsExactly<ConfigException>(() => Config.ParseListen(":70000", null));
    }
}
