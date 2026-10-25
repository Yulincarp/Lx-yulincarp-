using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lx.Probe.Diagnostics;
using Lx.Probe.Model;

namespace Lx.Probe.Auth;

// 账号存本地：minecraft/accounts.json。
// AccessToken 用 Windows DPAPI（CurrentUser）加密；旧明文文件第一次读取时自动迁移。
public static class AccountStore
{
    private const string ProtectedPrefix = "dpapi:";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string PathFor(string installRoot) => Path.Combine(installRoot, "accounts.json");

    public static List<Account> Load(string installRoot)
    {
        string path = PathFor(installRoot);
        if (!File.Exists(path))
        {
            return new List<Account>();
        }

        List<Account>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return new List<Account>(); // 文件坏了就当没有，不挡住启动
        }

        if (stored is null)
        {
            return new List<Account>();
        }

        bool needsMigration = false;
        var result = new List<Account>(stored.Count);
        foreach (Account account in stored)
        {
            string token = account.AccessToken ?? string.Empty;
            if (IsProtected(token))
            {
                token = Unprotect(token, account.Name, "AccessToken");
            }
            else if (token.Length > 0)
            {
                needsMigration = true;
            }

            string? refresh = account.RefreshToken;
            if (!string.IsNullOrEmpty(refresh))
            {
                if (IsProtected(refresh))
                {
                    refresh = Unprotect(refresh, account.Name, "RefreshToken");
                    if (refresh.Length == 0)
                    {
                        refresh = null;
                    }
                }
                else
                {
                    needsMigration = true;
                }
            }

            result.Add(account with { AccessToken = token, RefreshToken = refresh });
        }

        if (needsMigration && OperatingSystem.IsWindows())
        {
            TryMigrate(installRoot, path, result);
        }

        return result;
    }

    public static Account? FindByName(string installRoot, string name) =>
        Load(installRoot).FirstOrDefault(account =>
            string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase));

    // 同一个 type + name 覆盖，其他账号保留。
    public static void Save(string installRoot, Account account)
    {
        List<Account> accounts = Load(installRoot);
        accounts.RemoveAll(existing =>
            existing.Type == account.Type &&
            string.Equals(existing.Name, account.Name, StringComparison.OrdinalIgnoreCase));
        accounts.Add(account);

        SaveAll(installRoot, accounts);
    }

    private static void SaveAll(string installRoot, List<Account> accounts)
    {
        Directory.CreateDirectory(installRoot);

        List<Account> stored = accounts
            .Select(account => account with
            {
                AccessToken = Protect(account.AccessToken ?? string.Empty),
                RefreshToken = string.IsNullOrEmpty(account.RefreshToken)
                    ? account.RefreshToken
                    : Protect(account.RefreshToken),
            })
            .ToList();

        string path = PathFor(installRoot);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(stored, Options));
        File.Move(temp, path, overwrite: true);
    }

    private static void TryMigrate(string installRoot, string path, List<Account> accounts)
    {
        string backup = path + ".bak";
        try
        {
            File.Copy(path, backup, overwrite: true);
            SaveAll(installRoot, accounts);

            // 备份也覆盖成加密版本，别把旧明文 token 留在 .bak 里。
            File.Copy(path, backup, overwrite: true);
            Log.Info("accounts.json 已迁移为 DPAPI 加密");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            try
            {
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // 备份删不掉就算了，下次启动继续尝试迁移
            }

            Log.Warn($"accounts.json 迁移失败（下次启动再试）：{ex.Message}");
        }
    }

    private static bool IsProtected(string token) =>
        token.StartsWith(ProtectedPrefix, StringComparison.Ordinal);

    private static string Protect(string token)
    {
        if (token.Length == 0 || IsProtected(token) || !OperatingSystem.IsWindows())
        {
            return token;
        }

        try
        {
            byte[] cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(token),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(cipher);
        }
        catch (CryptographicException ex)
        {
            Log.Warn($"AccessToken 加密失败，按明文保存：{ex.Message}");
            return token;
        }
    }

    private static string Unprotect(string stored, string accountName, string fieldName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            byte[] cipher = Convert.FromBase64String(stored[ProtectedPrefix.Length..]);
            byte[] plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            Log.Warn($"账号 {accountName} 的 {fieldName} 无法解密（换了 Windows 用户/机器？），需要重新登录");
            return string.Empty;
        }
    }
}
