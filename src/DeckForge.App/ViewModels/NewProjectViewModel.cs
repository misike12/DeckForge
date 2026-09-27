using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class NewProjectViewModel : ObservableObject
{
    private readonly PluginProjectGenerator _generator;
    private readonly WorkspaceManager _workspaces;

    public NewProjectViewModel(PluginProjectGenerator generator, WorkspaceManager workspaces)
    {
        _generator = generator;
        _workspaces = workspaces;
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
    private string _validationSummary = "";

    [ObservableProperty]
    private bool _canCreate;

    [ObservableProperty]
    private string? _createdPath;

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
        if (!PluginIdTouched && string.IsNullOrWhiteSpace(PluginName))
        {
            // Publisher drives the id only once the name exists; keep preview responsive.
        }
        Validate();
    }

    partial void OnPluginIdChanged(string value) => Validate();

    partial void OnParentDirectoryChanged(string value) => Validate();

    public IEnumerable<string> SelectedPlatforms =>
        (new (bool Selected, string Rid)[]
        {
            (WinX64, "win-x64"),
            (WinArm64, "win-arm64"),
            (OsxArm64, "osx-arm64"),
            (LinuxX64, "linux-x64"),
        }).Where(p => p.Selected).Select(p => p.Rid);

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
        if (!string.IsNullOrWhiteSpace(Repository) && !Uri.TryCreate(Repository, UriKind.Absolute, out var uri))
        {
            problems.Add("Repository must be an absolute URL.");
        }

        ValidationSummary = problems.Count == 0 ? "Ready." : string.Join(" ", problems);
        CanCreate = problems.Count == 0;
    }
}
