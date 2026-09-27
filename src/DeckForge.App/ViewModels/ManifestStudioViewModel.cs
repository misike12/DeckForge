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

    /// <summary>True when the form differs from what is on disk.</summary>
    [ObservableProperty]
    private bool _isDirty;

    public ObservableCollection<ValidationIssue> Issues { get; } = [];

    public ObservableCollection<string> Platforms { get; } = [];

    public ObservableCollection<PermissionRow> Permissions { get; } = [];

    public void LoadFromWorkspace()
    {
        var ws = _workspaces.Current;
        if (ws is null || !File.Exists(ws.ManifestPath))
        {
            ClearDocument();
            ValidationSummary = ws is null
                ? "No workspace is open."
                : $"{ws.ManifestPath} does not exist.";
            return;
        }

        try
        {
            _document = ManifestDocument.Load(ws.ManifestPath);
            ManifestPath = ws.ManifestPath;
            HasDocument = true;
            LoadIntoFields();
            IsDirty = false;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // The previous manifest's fields used to stay on screen here, so a parse failure left
            // the form showing one manifest's values while claiming it could not read the file -
            // and Save would then have written those stale values over the real one.
            ClearDocument();
            ValidationSummary = "manifest.json could not be parsed; fix the JSON first. " + ex.Message;
        }
    }

    private void ClearDocument()
    {
        _document = null;
        HasDocument = false;
        ManifestPath = "";
        RawJson = "";
        Issues.Clear();
        _suppressRefresh = true;
        Platforms.Clear();
        Permissions.Clear();
        foreach (var group in PermissionGroups)
        {
            group.Recount();
        }

        _suppressRefresh = false;
        IsDirty = false;
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
        IsDirty = true;
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

        // Every group is recounted, not just the one that owns this permission: a custom
        // permission is not in any group, and the group rows are what the counters read.
        foreach (var group in PermissionGroups)
        {
            group.Recount();
        }
    }

    /// <summary>Called by the shell when the page is navigated to.</summary>
    /// <remarks>
    /// Navigating away and back reloaded from disk, which silently discarded every unsaved edit.
    /// A dirty form is left alone and says so, because throwing the work away is worse than showing
    /// a stale one.
    /// </remarks>
    public void RefreshOnNavigate()
    {
        if (IsDirty)
        {
            SaveStatus = "Unsaved changes are still here - press Save, or Reload to discard them.";
            return;
        }

        LoadFromWorkspace();
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
        IsDirty = false;
        SaveStatus = $"Saved {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>Discards unsaved edits and reads the manifest again.</summary>
    [RelayCommand]
    private void ReloadFromDisk()
    {
        IsDirty = false;
        LoadFromWorkspace();
        SaveStatus = "Reloaded from disk.";
    }

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

            // The timer fires on a thread-pool thread. Posting rather than Invoke means a slow
            // layout pass cannot block the timer thread, and posting when already on the UI thread
            // avoids the deadlock that blocking a dispatcher from inside a dispatcher call causes.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                RefreshValidation();
            }
            else
            {
                dispatcher.BeginInvoke(RefreshValidation);
            }
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
