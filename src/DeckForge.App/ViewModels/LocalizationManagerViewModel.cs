using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.Core.Workspace;
using DeckForge.Validators;

namespace DeckForge.App.ViewModels;

public partial class LocalizationKey : ObservableObject
{
    public LocalizationKey(string key, string defaultValue)
    {
        Key = key;
        DefaultValue = defaultValue;
    }

    public string Key { get; }
    public string Group => Key.Contains('.') ? Key[..Key.LastIndexOf('.')] : "(root)";
    public string Leaf => Key.Contains('.') ? Key[(Key.LastIndexOf('.') + 1)..] : Key;

    [ObservableProperty]
    private string _defaultValue;

    public ObservableCollection<TranslationRow> Translations { get; } = [];
}

public partial class TranslationRow : ObservableObject
{
    public TranslationRow(string cultureTag, string value, bool exists)
    {
        CultureTag = cultureTag;
        _value = value;
        Exists = exists;
    }

    public string CultureTag { get; }

    [ObservableProperty]
    private string _value;

    public bool Exists { get; }
}

public partial class LocalizationManagerViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private readonly ResxMergerService _resx;

    public LocalizationManagerViewModel(WorkspaceManager workspaces, ResxMergerService resx)
    {
        _workspaces = workspaces;
        _resx = resx;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private LocalizationKey? _selectedKey;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _diagnosticsSummary = "";

    public ObservableCollection<LocalizationKey> Keys { get; } = [];

    public ObservableCollection<ValidationIssue> Issues { get; } = [];

    public void Load()
    {
        var ws = _workspaces.Current;
        HasWorkspace = ws is not null;
        if (ws is null)
        {
            return;
        }
        LoadKeys(ws);
        RunDiagnostics(ws);
    }

    public void RefreshOnNavigate() => Load();

    private void LoadKeys(WorkspaceContext ws)
    {
        var defaultPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
        if (!File.Exists(defaultPath))
        {
            return;
        }

        var cultures = Directory.GetFiles(ws.LocalizationDirectory, "Strings.*.resx")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(f => f.StartsWith("Strings.", StringComparison.Ordinal) && f.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)
                ? f[8..^5]
                : null)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        var defaults = _resx.ReadKeys(defaultPath);
        Keys.Clear();
        foreach (var (key, value) in defaults.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var entry = new LocalizationKey(key, value);
            foreach (var culture in cultures)
            {
                var culturePath = Path.Combine(ws.LocalizationDirectory, $"Strings.{culture}.resx");
                var translations = File.Exists(culturePath) ? _resx.ReadKeys(culturePath) : new Dictionary<string, string>();
                entry.Translations.Add(new TranslationRow(culture, translations.GetValueOrDefault(key, ""), translations.ContainsKey(key)));
            }
            Keys.Add(entry);
        }
        SelectedKey = Keys.FirstOrDefault();
    }

    private void RunDiagnostics(WorkspaceContext ws)
    {
        var dir = new LocalizationDirectory(ws.PluginProjectDirectory);
        var result = LocalizationValidator.ValidateProject(dir);
        Issues.Clear();
        foreach (var issue in result.Issues)
        {
            Issues.Add(issue);
        }
        DiagnosticsSummary = result.ToString();
    }

    [RelayCommand]
    private void SaveTranslation(TranslationRow? row)
    {
        var ws = _workspaces.Current;
        if (ws is null || row is null || SelectedKey is null)
        {
            return;
        }
        var path = Path.Combine(ws.LocalizationDirectory, $"Strings.{row.CultureTag}.resx");
        _resx.AddKeys(path, new Dictionary<string, string> { [SelectedKey.Key] = row.Value });
        CodeGen.Generation.ResxMerger.SetKey(path, SelectedKey.Key, row.Value);
        StatusText = $"Saved {SelectedKey.Key} [{row.CultureTag}]";
        LoadKeys(ws);
        RunDiagnostics(ws);
    }

    [RelayCommand]
    private void AddKey()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            return;
        }
        var stem = $"Custom.Key{Keys.Count + 1}";
        _resx.AddKeys(Path.Combine(ws.LocalizationDirectory, "Strings.resx"), new Dictionary<string, string>
        {
            [stem] = "New value",
        });
        Load();
        StatusText = $"Added {stem} - rename and fill it in.";
    }
}
