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
    private bool _exists;

    public TranslationRow(string cultureTag, string value, bool exists)
    {
        CultureTag = cultureTag;
        _value = value;
        _exists = exists;
    }

    public string CultureTag { get; }

    [ObservableProperty]
    private string _value;

    /// <summary>Whether this culture actually declares the key, as opposed to showing the default.</summary>
    public bool Exists
    {
        get => _exists;
        private set
        {
            if (_exists != value)
            {
                _exists = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MissingLabel));
            }
        }
    }

    /// <summary>
    /// The "(missing)" suffix, empty once the translation exists.
    /// </summary>
    /// <remarks>
    /// A WPF <c>Run</c> has no Visibility of its own, so a DataTrigger is not an option on one.
    /// The label is empty rather than the marker being hidden for exactly that reason.
    /// </remarks>
    public string MissingLabel => Exists ? "" : "  (missing)";

    /// <summary>Records that this row now has a value, so the "(missing)" marker clears.</summary>
    public void MarkExists() => Exists = true;
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

        // Read every culture once, not once per key. This loop used to re-read and re-parse each
        // Strings.<culture>.resx inside the key loop, so a project with 500 keys in 5 cultures
        // parsed 2,500 XML documents to draw one screen.
        var byCulture = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var culture in cultures)
        {
            var culturePath = Path.Combine(ws.LocalizationDirectory, $"Strings.{culture}.resx");
            byCulture[culture] = File.Exists(culturePath)
                ? _resx.ReadKeys(culturePath)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var defaults = _resx.ReadKeys(defaultPath);
        Keys.Clear();
        foreach (var (key, value) in defaults.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var entry = new LocalizationKey(key, value);
            foreach (var (culture, translations) in byCulture)
            {
                entry.Translations.Add(new TranslationRow(
                    culture,
                    translations.GetValueOrDefault(key, ""),
                    translations.ContainsKey(key)));
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

        // One write, through the facade. This called AddKeys and then reached around it into the
        // static ResxMerger to call SetKey, so every save parsed and serialised the file twice -
        // and the two calls disagreed about what "save" meant, since AddKeys only fills gaps.
        _resx.SetKey(path, SelectedKey.Key, row.Value);
        row.MarkExists();

        StatusText = $"Saved {SelectedKey.Key} [{row.CultureTag}]";
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

        var stem = NextCustomKeyName();
        _resx.AddKeys(Path.Combine(ws.LocalizationDirectory, "Strings.resx"), new Dictionary<string, string>
        {
            [stem] = "New value",
        });

        Load();
        SelectedKey = Keys.FirstOrDefault(k => k.Key == stem);
        StatusText = $"Added {stem} - rename it, then fill in the translations.";
    }

    /// <summary>
    /// The first unused <c>Custom.KeyN</c>.
    /// </summary>
    /// <remarks>
    /// This used to be <c>Custom.Key{Keys.Count + 1}</c>, which collides as soon as a key is
    /// deleted: four keys, delete one, add one, and the new key reuses the deleted name and
    /// silently overwrites whatever took its place.
    /// </remarks>
    private string NextCustomKeyName()
    {
        var taken = Keys.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        for (var n = 1; ; n++)
        {
            var candidate = $"Custom.Key{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Renames the selected key across every resx that mentions it.</summary>
    [RelayCommand]
    private void RenameKey(string? newKey)
    {
        var ws = _workspaces.Current;
        if (ws is null || SelectedKey is null || string.IsNullOrWhiteSpace(newKey))
        {
            return;
        }

        var target = newKey.Trim();
        if (target == SelectedKey.Key)
        {
            return;
        }

        if (Keys.Any(k => string.Equals(k.Key, target, StringComparison.Ordinal)))
        {
            StatusText = $"{target} already exists.";
            return;
        }

        var oldKey = SelectedKey.Key;
        var defaultPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
        var renamed = _resx.RenameKey(defaultPath, oldKey, target) ? 1 : 0;

        foreach (var culture in Directory.GetFiles(ws.LocalizationDirectory, "Strings.*.resx"))
        {
            if (_resx.RenameKey(culture, oldKey, target))
            {
                renamed++;
            }
        }

        Load();
        SelectedKey = Keys.FirstOrDefault(k => k.Key == target);
        StatusText = renamed == 0
            ? $"{oldKey} was not present in any resx."
            : $"Renamed {oldKey} to {target} in {renamed} file(s).";
    }
}
