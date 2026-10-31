using Lx.Probe.Diagnostics;

namespace Lx.Tests;

public class LxExceptionTests
{
    [Fact]
    public void From_MapsCommonExceptions_ToKinds()
    {
        Assert.Equal(LxErrorKind.Cancelled, LxException.From(new OperationCanceledException(), "同步").Kind);
        Assert.Equal(LxErrorKind.Timeout, LxException.From(new TimeoutException(), "同步").Kind);
        Assert.Equal(LxErrorKind.Network, LxException.From(new HttpRequestException("连接被重置"), "同步").Kind);
        Assert.Equal(LxErrorKind.NotFound, LxException.From(new FileNotFoundException("x.jar"), "安装").Kind);
        Assert.Equal(LxErrorKind.Io, LxException.From(new IOException("磁盘满"), "下载").Kind);
        Assert.Equal(LxErrorKind.Io, LxException.From(new UnauthorizedAccessException(), "写入").Kind);
        Assert.Equal(LxErrorKind.InvalidConfig, LxException.From(new System.Text.Json.JsonException("bad"), "读清单").Kind);
        Assert.Equal(LxErrorKind.InvalidState, LxException.From(new InvalidOperationException("先登录"), "启动").Kind);
        Assert.Equal(LxErrorKind.Unknown, LxException.From(new ArgumentException("?"), "启动").Kind);
    }

    [Fact]
    public void From_KeepsUserMessageForInvalidState()
    {
        LxException error = LxException.From(new InvalidOperationException("微软登录已过期，请重新登录"), "启动游戏");

        Assert.Equal("微软登录已过期，请重新登录", error.UserMessage);
        Assert.Equal("启动游戏", error.Context);
    }

    [Fact]
    public void TraceId_IsShortAndPreservedWhenAlreadyWrapped()
    {
        LxException first = LxException.From(new HttpRequestException("boom"), "同步");

        Assert.Equal(8, first.TraceId.Length);
        Assert.True(first.TraceId.All(Uri.IsHexDigit));

        LxException again = LxException.From(first, "别的上下文");
        Assert.Same(first, again);
        Assert.Equal(first.TraceId, again.TraceId);
    }

    [Fact]
    public void Detail_ContainsKindTraceAndInnerChain()
    {
        var inner = new InvalidOperationException("底层失败");
        var wrapped = new LxException(LxErrorKind.Network, "同步整合包", "网络请求失败", "abcd1234", inner);

        string detail = wrapped.Detail;

        Assert.Contains("网络错误", detail);
        Assert.Contains("同步整合包", detail);
        Assert.Contains("abcd1234", detail);
        Assert.Contains("底层失败", detail);
    }

    [Fact]
    public void ToString_CarriesTraceId_SoLogsCanBeSearched()
    {
        LxException error = LxException.From(new IOException("disk"), "下载");

        Assert.Contains(error.TraceId, error.ToString());
        Assert.Contains("下载", error.ToString());
    }
}
