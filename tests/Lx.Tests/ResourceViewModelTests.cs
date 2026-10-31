using System.IO.Compression;
using Lx.Probe.Mods;
using Lx.Probe.ViewModels;

namespace Lx.Tests;

public class ResourceViewModelTests
{
    private static string TempGameDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "rvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ModManager.ModsDir(dir));
        return dir;
    }

    private static string WriteMod(string dir, string fileName, string modJson)
    {
        string path = Path.Combine(dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = zip.CreateEntry("fabric.mod.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(modJson);
        return path;
    }

    [Fact]
    public void Load_ReadsMods_AndExposesModsDirectory()
    {
        string game = TempGameDir();
        try
        {
            WriteMod(ModManager.ModsDir(game), "sodium.jar", """{"id":"sodium","name":"Sodium"}""");

            var vm = new ResourceViewModel();
            vm.Load(game);

            Assert.Single(vm.Mods);
            Assert.Equal("Sodium", vm.Mods[0].DisplayName);
            Assert.EndsWith("mods", vm.ModsDirectory, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void Search_FiltersByNameOrFileName()
    {
        string game = TempGameDir();
        try
        {
            WriteMod(ModManager.ModsDir(game), "sodium.jar", """{"id":"sodium","name":"Sodium"}""");
            WriteMod(ModManager.ModsDir(game), "iris.jar", """{"id":"iris","name":"Iris Shaders"}""");

            var vm = new ResourceViewModel();
            vm.Load(game);
            Assert.Equal(2, vm.Mods.Count);

            vm.Search = "iris";
            Assert.Single(vm.Mods);
            Assert.Equal("Iris Shaders", vm.Mods[0].DisplayName);

            vm.Search = string.Empty;
            Assert.Equal(2, vm.Mods.Count);
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void SortIndex_ChangesOrder()
    {
        string game = TempGameDir();
        try
        {
            WriteMod(ModManager.ModsDir(game), "b.jar", """{"id":"zeta","name":"Zeta"}""");
            WriteMod(ModManager.ModsDir(game), "a.jar", """{"id":"alpha","name":"Alpha"}""");

            var vm = new ResourceViewModel();
            vm.Load(game);

            // 默认按显示名
            Assert.Equal("Alpha", vm.Mods[0].DisplayName);

            vm.SortIndex = 1; // 按文件名
            Assert.Equal("a.jar", vm.Mods[0].FileName);
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void SetEnabled_TogglesFileSuffix()
    {
        string game = TempGameDir();
        try
        {
            WriteMod(ModManager.ModsDir(game), "sodium.jar", """{"id":"sodium","name":"Sodium"}""");

            var vm = new ResourceViewModel();
            vm.Load(game);

            vm.SetEnabled(vm.Mods[0], false);
            Assert.False(vm.Mods[0].Enabled);
            Assert.True(File.Exists(Path.Combine(ModManager.ModsDir(game), "sodium.jar.disabled")));

            vm.SetEnabled(vm.Mods[0], true);
            Assert.True(vm.Mods[0].Enabled);
            Assert.True(File.Exists(Path.Combine(ModManager.ModsDir(game), "sodium.jar")));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void AddFiles_AddsNew_SkipsDuplicate()
    {
        string game = TempGameDir();
        string source = TempGameDir();
        try
        {
            string jar = WriteMod(source, "sodium.jar", """{"id":"sodium","name":"Sodium"}""");

            var vm = new ResourceViewModel();
            vm.Load(game);

            ModManager.AddManyResult first = vm.AddFiles(new[] { jar });
            Assert.Equal(1, first.Added);
            Assert.Single(vm.Mods);

            ModManager.AddManyResult again = vm.AddFiles(new[] { jar });
            Assert.Equal(0, again.Added);
            Assert.Equal(1, again.Skipped);
            Assert.Single(vm.Mods);
        }
        finally
        {
            Directory.Delete(game, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void Remove_DeletesFile_AndReloads()
    {
        string game = TempGameDir();
        try
        {
            WriteMod(ModManager.ModsDir(game), "sodium.jar", """{"id":"sodium","name":"Sodium"}""");

            var vm = new ResourceViewModel();
            vm.Load(game);
            Assert.True(vm.Remove(vm.Mods[0]));

            Assert.Empty(vm.Mods);
            Assert.False(File.Exists(Path.Combine(ModManager.ModsDir(game), "sodium.jar")));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }
}
