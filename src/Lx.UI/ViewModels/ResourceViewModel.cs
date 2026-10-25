using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Lx.Probe.Mods;

namespace Lx.UI.ViewModels;

// H3：资源页本地 mods 的筛选/排序/增删状态放在 ViewModel，
// MainWindow 只负责把当前游戏目录交给它。
public sealed class ResourceViewModel : INotifyPropertyChanged
{
    private readonly List<ModFile> _all = new();
    private string _gameDir = string.Empty;
    private string _search = string.Empty;
    private int _sortIndex;
    private string _modsDirectory = string.Empty;

    public ObservableCollection<ModFile> Mods { get; } = new();

    public string Search
    {
        get => _search;
        set
        {
            if (_search == value) return;
            _search = value ?? string.Empty;
            OnPropertyChanged();
            Apply();
        }
    }

    public int SortIndex
    {
        get => _sortIndex;
        set
        {
            if (_sortIndex == value) return;
            _sortIndex = value;
            OnPropertyChanged();
            Apply();
        }
    }

    public string ModsDirectory
    {
        get => _modsDirectory;
        private set
        {
            if (_modsDirectory == value) return;
            _modsDirectory = value;
            OnPropertyChanged();
        }
    }

    public void Load(string gameDir)
    {
        _gameDir = gameDir;
        string dir = ModManager.ModsDir(gameDir);
        Directory.CreateDirectory(dir);
        ModsDirectory = dir;
        _all.Clear();
        _all.AddRange(ModManager.List(gameDir));
        Apply();
    }

    public void SetEnabled(ModFile mod, bool enabled)
    {
        if (_gameDir.Length == 0) return;
        ModManager.SetEnabled(_gameDir, mod.FileName, enabled);
        Load(_gameDir);
    }

    public bool Remove(ModFile mod)
    {
        if (_gameDir.Length == 0) return false;
        bool removed = ModManager.Remove(_gameDir, mod.FileName);
        if (removed)
        {
            Load(_gameDir);
        }

        return removed;
    }

    private void Apply()
    {
        IEnumerable<ModFile> filtered = _all;
        string query = _search.Trim();
        if (query.Length > 0)
        {
            filtered = filtered.Where(mod =>
                mod.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                mod.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (mod.Id ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filtered = _sortIndex switch
        {
            1 => filtered.OrderBy(mod => mod.FileName, StringComparer.OrdinalIgnoreCase),
            2 => filtered.OrderByDescending(mod => mod.Enabled).ThenBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase),
        };

        Mods.Clear();
        foreach (ModFile mod in filtered)
        {
            Mods.Add(mod);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
