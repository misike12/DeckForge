using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class NewProjectViewModel : ObservableObject
{
    private readonly PluginProjectGenerator _generator;
    private readonly WorkspaceManager _workspaces;
    private readonly SettingsService _settings;

    public NewProjectViewModel(
        PluginProjectGenerator generator,
        WorkspaceManager workspaces,
        SettingsService settings)
    {
        _generator = generator;
        _workspaces = workspaces;
        _settings = settings;

        // The wizard's starting folder is a setting. It was stored and never read, so the wizard
        // always opened on Documents no matter what the user had chosen here.
        _parentDirectory = FirstExistingDirectory(
            settings.Settings.DefaultProjectsDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        // The definition owns the tick so the wizard and the Capabilities page cannot disagree
        // about it; the view model keeps the id list the generator actually needs.
        foreach (var capability in AvailableCapabilities)
        {
            capability.SelectedChanged += (_, _) => CollectSelectedCapabilities();
        }

        Validate();
    }

    /// <summary>The first of the candidates that exists, or the first one, or Documents.</summary>
    private static string FirstExistingDirectory(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    [ObservableProperty]
    private string _pluginName = "";

    [ObservableProperty]
    private string _pluginId = "";

    [ObservableProperty]
    private bool _pluginIdTouched;

    [ObservableProperty]
    private string _publisher = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _parentDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    private string _repository = "";

    [ObservableProperty]
    private string _homepage = "";

    [ObservableProperty]
    private string _license = "MIT";

    /// <summary>Remember this folder as the wizard's default. Off by default: most people do not.</summary>
    [ObservableProperty]
    private bool _rememberLocation;

    [ObservableProperty]
    private bool _winX64 = true;

    [ObservableProperty]
    private bool _winArm64;

    [ObservableProperty]
    private bool _osxArm64;

    [ObservableProperty]
    private bool _linuxX64;

    [ObservableProperty]
    private bool _initGit = true;

    [ObservableProperty]
    private bool _selfContained;

    [ObservableProperty]
    private string _validationSummary = "";

    [ObservableProperty]
    private bool _canCreate;

    [ObservableProperty]
    private string? _createdPath;

    /// <summary>Every capability that can be opted into at creation, for the picker.</summary>
    public IReadOnlyList<CodeGen.Capabilities.CapabilityDefinition> AvailableCapabilities =>
        CodeGen.Capabilities.CapabilityDefinitions.All;

    /// <summary>
    /// The capability ids ticked in the picker.
    /// </summary>
    /// <remarks>
    /// These were never collected and never passed on, so the wizard produced the same project
    /// whatever the user picked - and the contributor that turns them into code had nothing to
    /// act on.
    /// </remarks>
    public ObservableCollection<string> SelectedCapabilityIds { get; } = [];

    /// <summary>The languages to scaffold, as resx files beside Strings.resx.</summary>
    public ObservableCollection<string> SelectedLanguages { get; } = [];

    /// <summary>The culture tags offered by the picker, with their check state.</summary>
    public IReadOnlyList<LanguageToggle> PresetLanguageTags { get; } =
        new[] { "en-US", "de-DE", "fr-FR", "es-ES", "nl-NL", "pl-PL" }
            .Select(tag => new LanguageToggle(tag))
            .ToList();

    private void CollectSelectedCapabilities()
    {
        SelectedCapabilityIds.Clear();
        foreach (var capability in AvailableCapabilities.Where(c => c.IsSelected))
        {
            SelectedCapabilityIds.Add(capability.Id);
        }
    }

    partial void OnPluginNameChanged(string value)
    {
        if (!PluginIdTouched)
        {
            PluginId = Core.Utils.MacroDeckRules.SuggestPluginId(value, Publisher);
        }

        Validate();
    }

    partial void OnPublisherChanged(string value)
    {
        // The publisher is half the id. Until this was filled in, changing the publisher after
        // typing a name left a stale "com.oldpublisher.my-plugin" with no indication why.
        if (!PluginIdTouched)
        {
            PluginId = Core.Utils.MacroDeckRules.SuggestPluginId(PluginName, value);
        }

        Validate();
    }

    partial void OnPluginIdChanged(string value) => Validate();

    partial void OnParentDirectoryChanged(string value) => Validate();

    // Every platform toggle has to revalidate: CanCreate stayed true with no platform selected
    // because only the text fields were wired to Validate.
    partial void OnWinX64Changed(bool value) => Validate();
    partial void OnWinArm64Changed(bool value) => Validate();
    partial void OnOsxArm64Changed(bool value) => Validate();
    partial void OnLinuxX64Changed(bool value) => Validate();
    partial void OnRepositoryChanged(string value) => Validate();

    public IEnumerable<string> SelectedPlatforms =>
        (new (bool Selected, string Rid)[]
        {
            (WinX64, "win-x64"),
            (WinArm64, "win-arm64"),
            (OsxArm64, "osx-arm64"),
            (LinuxX64, "linux-x64"),
        }).Where(p => p.Selected).Select(p => p.Rid);

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    private CancellableOperation? _operation;

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    public CancellableOperation Operation =>
        _operation ??= new CancellableOperation().Track(CreateCommand);
    
    [RelayCommand]
    private void TouchPluginId() => PluginIdTouched = true;

    [RelayCommand]
    private async Task CreateAsync(CancellationToken ct)
    {
        var options = new Core.Plugins.NewProjectOptions
        {
            PluginName = PluginName.Trim(),
            PluginId = PluginId.Trim(),
            Publisher = Publisher.Trim(),
            Description = Description.Trim(),
            ParentDirectory = ParentDirectory.Trim(),
            Repository = string.IsNullOrWhiteSpace(Repository) ? null : Repository.Trim(),
            Homepage = string.IsNullOrWhiteSpace(Homepage) ? null : Homepage.Trim(),
            License = string.IsNullOrWhiteSpace(License) ? "MIT" : License.Trim(),
            Platforms = [.. SelectedPlatforms],
            SelfContained = SelfContained,
            Languages = [.. PresetLanguageTags.Where(l => l.IsChecked).Select(l => l.CultureTag)],
            CapabilityPresets = [.. SelectedCapabilityIds],
            InitGit = InitGit,
        };

        try
        {
            CreatedPath = await Task.Run(() => _generator.Generate(options), ct);
            ValidationSummary = $"Created {CreatedPath}";

            // Open the new plugin immediately so Build & Run / Manifest work right away.
            var solutionPath = Path.Combine(CreatedPath, options.FolderName + ".slnx");
            if (File.Exists(solutionPath))
            {
                if (RememberLocation)
                {
                    _settings.Update(s => s.DefaultProjectsDirectory = ParentDirectory.Trim());
                }

                var context = WorkspaceManager.FromPluginProject(
                    Path.Combine(CreatedPath, "src", options.FolderName));
                context.SolutionPath = solutionPath;
                _workspaces.Open(context);
                Services.ShellMessenger.NotifyWorkspaceChanged(context.SolutionPath);
                Services.ShellMessenger.NavigateTo("buildrun");
            }
        }
        catch (GenerationException ex)
        {
            ValidationSummary = ex.Message;
        }
        catch (OperationCanceledException)
        {
            ValidationSummary = "Cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only GenerationException was caught, so a read-only destination or a path that is
            // not a directory surfaced as an unhandled exception and the app closed.
            ValidationSummary = $"Could not create the project: {ex.Message}";
        }
    }

    private void Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(PluginName))
        {
            problems.Add("Name is required.");
        }

        if (!Core.Utils.MacroDeckRules.IsValidPluginId(PluginId))
        {
            problems.Add("Id must look like com.publisher.my-plugin.");
        }

        if (string.IsNullOrWhiteSpace(Publisher))
        {
            problems.Add("Publisher is required by the Store.");
        }

        if (string.IsNullOrWhiteSpace(ParentDirectory) || !Directory.Exists(ParentDirectory))
        {
            problems.Add("Choose an existing folder for the project.");
        }

        if (!SelectedPlatforms.Any())
        {
            problems.Add("Pick at least one platform.");
        }
        if (!string.IsNullOrWhiteSpace(Repository) && !Uri.TryCreate(Repository, UriKind.Absolute, out _))
        {
            problems.Add("Repository must be an absolute URL.");
        }

        ValidationSummary = problems.Count == 0 ? "Ready." : string.Join(" ", problems);
        CanCreate = problems.Count == 0;
    }
}

/// <summary>One culture tag in the wizard's language picker.</summary>
public sealed partial class LanguageToggle(string cultureTag) : ObservableObject
{
    public string CultureTag { get; } = cultureTag;

    [ObservableProperty]
    private bool _isChecked;
}
