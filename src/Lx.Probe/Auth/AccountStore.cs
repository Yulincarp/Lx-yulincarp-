using System.Text.Json;
using Lx.Probe.Model;

namespace Lx.Probe.Auth;

// 账号存本地：minecraft/accounts.json（明文，仅本机；密码永远不落盘）。
public static class AccountStore
{
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

        try
        {
            return JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(path), Options)
                   ?? new List<Account>();
        }
        catch (JsonException)
        {
            return new List<Account>(); // 文件坏了就当没有，不挡住启动
        }
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

        Directory.CreateDirectory(installRoot);
        File.WriteAllText(PathFor(installRoot), JsonSerializer.Serialize(accounts, Options));
    }
}
