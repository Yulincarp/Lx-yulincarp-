using Lx.Probe;

namespace Lx.Tests;

public class AppPathsTests
{
    [Fact]
    public void PortableMode_UsesDataNextToExe()
    {
        string root = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "portable.txt"), string.Empty);
            string install = AppPaths.ResolveFor(root, Path.Combine(root, "appdata"));
            Assert.Equal(Path.Combine(root, "data"), install);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void DevBuild_FindsRepositoryMinecraft()
    {
        string root = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "Lx.sln"), string.Empty);
            string bin = Path.Combine(root, "src", "Lx.UI", "bin", "Debug", "net8.0-windows");
            Directory.CreateDirectory(bin);

            string install = AppPaths.ResolveFor(bin, Path.Combine(root, "appdata"));
            Assert.Equal(Path.Combine(root, "minecraft"), install);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void PublishedBuild_UsesAppData()
    {
        string root = NewDir();
        try
        {
            string app = Path.Combine(root, "app");
            string appData = Path.Combine(root, "appdata");
            Directory.CreateDirectory(app);

            string install = AppPaths.ResolveFor(app, appData);
            Assert.Equal(Path.Combine(appData, "Lx"), install);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void PublishedWithoutAppData_FallsBackToData()
    {
        string root = NewDir();
        try
        {
            string app = Path.Combine(root, "app");
            Directory.CreateDirectory(app);

            string install = AppPaths.ResolveFor(app, string.Empty);
            Assert.Equal(Path.Combine(app, "data"), install);
        }
        finally
        {
            Delete(root);
        }
    }

    private static string NewDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "apppaths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清不掉不影响测试
        }
    }
}
