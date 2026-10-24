using System.Security.Cryptography;
namespace Lx.Probe.Resolve;

// M2 Step 2：把清单里的文件下到盘上，逐个校验 sha1。
public static class Downloader
{
    public static int DownloadAll(List<DownloadItem> items, string installRoot)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(120);
        int ok = 0;
        int skipped = 0;
        int failed = 0;
        foreach (DownloadItem item in items)
        {
                    string dest = Path.Combine(installRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(dest) && string.Equals(Sha1Of(dest), item.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            Console.WriteLine(dest);
                        string temp = dest + ".part";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
                using var response = http.Send(request);
                if (!response.IsSuccessStatusCode)
                {
                    failed++;
                    Console.WriteLine($"[失败] {(int)response.StatusCode} {item.Url}");
                    continue;
                }

                using (Stream body = response.Content.ReadAsStream())
                using (FileStream output = File.Create(temp))
                {
                    body.CopyTo(output);
                }

                if (!string.Equals(Sha1Of(temp), item.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    failed++;
                    Console.WriteLine($"[sha1 不符] {item.Url}");
                    continue;
                }

                File.Move(temp, dest, overwrite: true);
                ok++;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                failed++;
                Console.WriteLine($"[异常] {ex.GetType().Name} {item.Url}");
            }
            }
            Console.WriteLine($"成功 {ok} / 跳过 {skipped} / 失败 {failed}");
            return failed;
    }
    private static string Sha1Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}