using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.Core.Plugins;
using DeckForge.Core.Workspace;
using DeckForge.Validators;

namespace DeckForge.App.ViewModels;

public partial class ManifestStudioViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private ManifestDocument? _document;

    public ManifestStudioViewModel(WorkspaceManager workspaces)
    {
        _workspaces = workspaces;
        Services.ShellMessenger.WorkspaceChanged += _ => LoadFromWorkspace();
    }

    /// <summary>Permission groups shown as collapsible sections on the Manifest page.</summary>
    public IReadOnlyList<PermissionGroupVM> PermissionGroups { get; } =
    [
        new(PermissionGroup.Host),
        new(PermissionGroup.Publishing),
        new(PermissionGroup.System),
    ];

    [ObservableProperty]
    private bool _hasDocument;

    [ObservableProperty]
    private string _manifestPath = "";

    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _version = "1.0.0";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _iconPath = "Assets/icon.svg";

    [ObservableProperty]
    private string _publisher = "";

    [ObservableProperty]
    private string _license = "MIT";

    [ObservableProperty]
    private string _repository = "";

    [ObservableProperty]
    private string _homepage = "";

    [ObservableProperty]
    private string _macroDeckRange = MacroDeckSdkInfo.DefaultMacroDeckRange;

    [ObservableProperty]
    private string _rawJson = "";

    [ObservableProperty]
    private string _validationSummary = "";

    [ObservableProperty]
    private string _saveStatus = "";

    public ObservableCollection<ValidationIssue> Issues { get; } = [];

    public ObservableCollection<string> Platforms { get; } = [];

    public ObservableCollection<PermissionRow> Permissions { get; } = [];

    public void LoadFromWorkspace()
    {
        var ws = _workspaces.Current;
        if (ws is null || !File.Exists(ws.ManifestPath))
        {
            _document = null;
            HasDocument = false;
            return;
        }
        try
        {
            _document = ManifestDocument.Load(ws.ManifestPath);
            ManifestPath = ws.ManifestPath;
            HasDocument = true;
            LoadIntoFields();
        }
        catch (Exception)
        {
            _document = null;
            HasDocument = false;
            ValidationSummary = "manifest.json could not be parsed; fix the JSON first.";
        }
    }

    private void LoadIntoFields()
    {
        var doc = _document!;
        _suppressRefresh = true;
        Id = doc.Id;
        Name = doc.Name;
        Version = doc.Version;
        Description = doc.RawDocument["description"]?.GetValue<string>() ?? "";
        IconPath = doc.RawDocument["icon"]?.GetValue<string>() ?? "";
        Publisher = doc.PublisherName;
        License = doc.RawDocument["license"]?.GetValue<string>() ?? "";
        Repository = doc.RawDocument["repository"]?.GetValue<string>() ?? "";
        Homepage = doc.RawDocument["homepage"]?.GetValue<string>() ?? "";
        MacroDeckRange = doc.RawDocument["compatibility"]?["macroDeck"]?.GetValue<string>()
            ?? MacroDeckSdkInfo.DefaultMacroDeckRange;
        RawJson = doc.ToJson();

        Platforms.Clear();
        foreach (var rid in doc.Platforms)
        {
            Platforms.Add(rid);
        }

        Permissions.Clear();
        foreach (var perm in doc.Permissions.Where(p => Core.Plugins.PermissionCatalog.ExplanationOf(p) is null))
        {
            // Custom (unknown-to-the-catalog) permissions still surface, flagged as such.
            Permissions.Add(new PermissionRow(perm, true, TogglePermission, isCustom: true));
        }

        var enabled = doc.Permissions.ToHashSet(StringComparer.Ordinal);
        foreach (var group in PermissionGroups)
        {
            group.Refresh(enabled, TogglePermission);
        }

        _suppressRefresh = false;
        RefreshValidation();
    }

    private void Apply(Action<ManifestDocument> apply)
    {
        if (_document is null)
        {
            return;
        }
        apply(_document);
        QueueRefresh();
    }

    partial void OnIdChanged(string value) => Apply(d => d.SetId(value.Trim()));
    partial void OnNameChanged(string value) => Apply(d => d.SetName(value));
    partial void OnVersionChanged(string value) => Apply(d => d.SetVersion(value.Trim()));
    partial void OnDescriptionChanged(string value) => Apply(d => d.SetDescription(value));
    partial void OnIconPathChanged(string value) => Apply(d => d.SetIcon(value.Trim()));
    partial void OnPublisherChanged(string value) => Apply(d => d.SetPublisherName(value.Trim()));
    partial void OnLicenseChanged(string value) => Apply(d => d.SetLicense(string.IsNullOrWhiteSpace(value) ? null : value.Trim()));
    partial void OnRepositoryChanged(string value) => Apply(d => d.SetRepository(string.IsNullOrWhiteSpace(value) ? null : value.Trim()));
    partial void OnHomepageChanged(string value) => Apply(d => d.SetHomepage(string.IsNullOrWhiteSpace(value) ? null : value.Trim()));

    partial void OnMacroDeckRangeChanged(string value) => Apply(d =>
    {
        var compat = d.RawDocument["compatibility"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
        compat["macroDeck"] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        d.RawDocument["compatibility"] = compat;
    });

    private void TogglePermission(string permission, bool enabled)
    {
        Apply(d =>
        {
            var current = d.Permissions.ToList();
            if (enabled && !current.Contains(permission, StringComparer.Ordinal))
            {
                current.Add(permission);
            }
            else if (!enabled)
            {
                current.RemoveAll(p => string.Equals(p, permission, StringComparison.Ordinal));
            }
            d.SetPermissions([.. current]);
        });

        foreach (var group in PermissionGroups)
        {
            var row = group.Rows.FirstOrDefault(r => r.Name == permission);
            if (row is not null)
            {
                group.NotifyToggled(enabled);
            }
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_document is null)
        {
            return;
        }
        _document.Save(ManifestPath);
        RawJson = _document.ToJson();
        SaveStatus = $"Saved {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private void ReloadFromDisk() => LoadFromWorkspace();

    private bool _suppressRefresh;
    private System.Timers.Timer? _refreshTimer;

    /// <summary>Debounced re-validation so each keystroke validates without thrashing.</summary>
    private void QueueRefresh()
    {
        if (_suppressRefresh)
        {
            return;
        }
        _refreshTimer?.Dispose();
        _refreshTimer = new System.Timers.Timer(350) { AutoReset = false };
        _refreshTimer.Elapsed += (_, _) =>
        {
            _refreshTimer.Dispose();
            _refreshTimer = null;
            System.Windows.Application.Current?.Dispatcher.Invoke(RefreshValidation);
        };
        _refreshTimer.Start();
    }

    private void RefreshValidation()
    {
        if (_document is null)
        {
            return;
        }
        var result = ManifestValidator.Validate(_document.ToJson());
        Issues.Clear();
        foreach (var issue in result.Issues)
        {
            Issues.Add(issue);
        }
        ValidationSummary = result.ToString();
        RawJson = _document.ToJson();
    }
}

/// <summary>One checkable permission with its explanation; checkbox state is rebound per load.</summary>
public sealed class PermissionRow : System.ComponentModel.INotifyPropertyChanged
{
    private Action<string, bool> _toggle;
    private bool _enabled;

    public PermissionRow(string name, bool enabled, Action<string, bool> toggle, bool isCustom = false)
    {
        Name = name;
        _enabled = enabled;
        _toggle = toggle;
        IsCustom = isCustom;
        Explanation = Core.Plugins.PermissionCatalog.ExplanationOf(name)
            ?? (isCustom ? "Custom permission - not in the known vocabulary. The host shows it to the user but ignores it (unknown-permission warning at validate)." : "");
    }

    public string Name { get; }
    public string Explanation { get; }
    public bool IsCustom { get; }

    public bool Enabled
    {
        get => _enabled;
        private set
        {
            if (_enabled != value)
            {
                _enabled = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Enabled)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Rebinds the row to the current manifest state without firing the toggle.</summary>
    public void Rebind(bool enabled, Action<string, bool> toggle)
    {
        _toggle = toggle;
        Enabled = enabled;
    }

    /// <summary>Called by the page's Checked/Unchecked handlers.</summary>
    public void SetEnabled(bool value) => _toggle(Name, value);
}

/// <summary>One grouped permission section: title, summary, and its rows.</summary>
public partial class PermissionGroupVM : ObservableObject
{
    public PermissionGroupVM(PermissionGroup group)
    {
        Group = group;
        Title = Core.Plugins.PermissionCatalog.Title(group);
        Summary = Core.Plugins.PermissionCatalog.GroupSummary(group);
        Rows = [.. Core.Plugins.PermissionCatalog.OfGroup(group)
            .Select(p => new PermissionRow(p.Name, false, (_, _) => { }))];
    }

    public PermissionGroup Group { get; }
    public string Title { get; }
    public string Summary { get; }

    public IReadOnlyList<PermissionRow> Rows { get; }

    [ObservableProperty]
    private int _enabledCount;

    public string CountLabel => EnabledCount == 0 ? "" : $"({EnabledCount} enabled)";

    public string GroupBrushKey => Group switch
    {
        PermissionGroup.Host => "Liquid.AccentBrush",
        PermissionGroup.Publishing => "Liquid.SuccessBrush",
        _ => "Liquid.WarningBrush",
    };

    partial void OnEnabledCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    /// <summary>Rebuilds the checked state of every row against the manifest's permission set.</summary>
    public void Refresh(HashSet<string> enabled, Action<string, bool> toggle)
    {
        foreach (var row in Rows)
        {
            row.Rebind(enabled.Contains(row.Name), toggle);
        }
        EnabledCount = Rows.Count(r => r.Enabled);
    }

    /// <summary>Called after a row toggles so the group counter stays current.</summary>
    public void NotifyToggled(bool enabled) => EnabledCount = Math.Max(0, EnabledCount + (enabled ? 1 : -1));
}
