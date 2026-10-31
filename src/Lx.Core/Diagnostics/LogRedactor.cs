using System.Text.RegularExpressions;

namespace Lx.Probe.Diagnostics;

// 日志脱敏：token / 密钥不能落盘，否则用户把日志发给别人就等于交出了账号。
// 只做保守匹配（明确的键名或 JWT 形状），宁可少删也不乱改正常文本。
public static class LogRedactor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex[] Rules =
    {
        // Authorization: Bearer xxx
        new(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.Compiled, Timeout),

        // access_token=xxx / "refreshToken":"xxx" 这类键值对
        new(@"(?i)\b(access[_-]?token|refresh[_-]?token|authorization|client[_-]?secret|api[_-]?key)\b\s*[=:]\s*""?[A-Za-z0-9._~+/=-]{6,}""?",
            RegexOptions.Compiled, Timeout),

        // JSON 里驼峰/下划线的 token 字段
        new(@"(?i)""(accessToken|refreshToken|access_token|refresh_token|clientSecret)""\s*:\s*""[^""]*""",
            RegexOptions.Compiled, Timeout),

        // JWT（微软/外置登录的 id_token 长这样）
        new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{4,}\b", RegexOptions.Compiled, Timeout),
    };

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        string result = text;
        foreach (Regex rule in Rules)
        {
            try
            {
                result = rule.Replace(result, match =>
                {
                    // 保留键名，值换成 ***，方便看出"这里本来有个 token"
                    string key = match.Groups.Count > 1 && match.Groups[1].Success
                        ? match.Groups[1].Value
                        : string.Empty;
                    return key.Length > 0 ? $"{key}=***" : "***";
                });
            }
            catch (RegexMatchTimeoutException)
            {
                // 超长/异常文本：宁可整段丢掉这部分也不卡住
                return "[日志脱敏超时，本段已省略]";
            }
        }

        return result;
    }
}
