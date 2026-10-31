using System.Text.Json;
using Lx.Probe.Diagnostics;

namespace Lx.Tests;

public class LogRedactionTests
{
    [Theory]
    [InlineData("access_token=abcdef1234567890", "abcdef1234567890", "access_token")]
    [InlineData("refresh_token=xyz987654321", "xyz987654321", "refresh_token")]
    [InlineData("client_secret=s3cr3t-value", "s3cr3t-value", "client_secret")]
    [InlineData("api_key=ABCdef123456", "ABCdef123456", "api_key")]
    public void Redact_RemovesSecretValues_ButKeepsKeyName(string input, string secret, string keyName)
    {
        string output = LogRedactor.Redact(input);

        Assert.DoesNotContain(secret, output);
        Assert.Contains(keyName, output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redact_AuthorizationBearerHeader_RemovesToken()
    {
        string output = LogRedactor.Redact("Authorization: Bearer eyJhbGciOiJSUzI1NiJ9.payload.signature");

        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiJ9", output);
        Assert.Contains("***", output);
    }

    [Fact]
    public void Redact_HandlesJsonStyleTokens()
    {
        string json = """{"name":"Steve","accessToken":"0123456789abcdef0123456789abcdef","refreshToken":"rt-123456"}""";

        string output = LogRedactor.Redact(json);

        Assert.DoesNotContain("0123456789abcdef", output);
        Assert.DoesNotContain("rt-123456", output);
        Assert.Contains("Steve", output);
    }

    [Fact]
    public void Redact_LeavesNormalTextAlone()
    {
        const string text = "已创建实例：生存（1.20.4）→ C:\\Users\\me\\instances\\生存";

        Assert.Equal(text, LogRedactor.Redact(text));
    }

    [Fact]
    public void Redact_NullOrEmpty_IsSafe()
    {
        Assert.Equal(string.Empty, LogRedactor.Redact(null));
        Assert.Equal(string.Empty, LogRedactor.Redact(""));
    }

    [Fact]
    public void JsonLine_IsValidJson_WithTraceAndRedactedMessage()
    {
        var error = new LxException(LxErrorKind.Network, "同步", "连不上 https://example.com?access_token=abcdef123456", "abcd1234");

        string line = Log.FormatJsonLine("ERROR", "同步失败 access_token=abcdef123456", error, new DateTime(2026, 10, 31, 13, 20, 0));

        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;

        Assert.Equal("ERROR", root.GetProperty("level").GetString());
        Assert.Equal("abcd1234", root.GetProperty("trace").GetString());
        Assert.Contains("同步失败", root.GetProperty("msg").GetString());
        Assert.DoesNotContain("abcdef123456", line);
        Assert.Equal("LxException", root.GetProperty("ex").GetProperty("type").GetString());
    }

    [Fact]
    public void JsonLine_WithoutException_HasNoTraceField()
    {
        string line = Log.FormatJsonLine("INFO", "启动", null, DateTime.Now);

        using JsonDocument doc = JsonDocument.Parse(line);
        Assert.False(doc.RootElement.TryGetProperty("trace", out _));
        Assert.False(doc.RootElement.TryGetProperty("ex", out _));
    }
}
