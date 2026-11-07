using Lx.Probe.Net;
using Xunit;

namespace Lx.Tests;

public sealed class ProxyBypassTests
{
    [Theory]
    [InlineData("yulincarp.dpdns.org")]
    [InlineData("mc.yulincarp.dpdns.org")]
    [InlineData("DPDNS.ORG")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void MirrorHosts_BypassProxy(string host) =>
        Assert.True(ProxyConfig.ShouldBypass(host), host + " 应该绕过代理");

    [Theory]
    [InlineData("github.com")]
    [InlineData("api.modrinth.com")]
    [InlineData("notdpdns.org")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherHosts_UseProxy(string? host) =>
        Assert.False(ProxyConfig.ShouldBypass(host), host + " 不该绕过代理");

    [Fact]
    public void BypassList_CoversMirrorAndLocal()
    {
        Assert.Contains("*.dpdns.org", ProxyConfig.BypassHosts);
        Assert.Contains("127.0.0.1", ProxyConfig.BypassHosts);
    }
}
