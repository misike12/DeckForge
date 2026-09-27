using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.Core.Capabilities;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class CapabilityGalleryViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private readonly CapabilityScaffolder _scaffolder;

    public CapabilityGalleryViewModel(WorkspaceManager workspaces, CapabilityScaffolder scaffolder)
    {
        _workspaces = workspaces;
        _scaffolder = scaffolder;
        Services.ShellMessenger.WorkspaceChanged += _ => RefreshWorkspaceState();
        RefreshWorkspaceState();
        BuildGroups();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<CapabilityGroup> Groups { get; } = [];

    private void RefreshWorkspaceState() => HasWorkspace = _workspaces.Current is not null;

    private void BuildGroups()
    {
        foreach (var category in Enum.GetValues<CapabilityCategory>())
        {
            var group = new CapabilityGroup(category.ToString()[..1] + category.ToString()[1..].Replace("And", " & "));
            foreach (var capability in CapabilityCatalog.All.Where(c => c.Category == category))
            {
                group.Cards.Add(new CapabilityCard(capability));
            }
            Groups.Add(group);
        }
    }

    [RelayCommand]
    private void OpenDocs(CapabilityCard card)
    {
        Services.ShellMessenger.NavigateTo($"docs::{card.Descriptor.DocsPath}");
    }

    /// <summary>Adds the capability to the open plugin where a scaffolder exists.</summary>
    [RelayCommand]
    private void Scaffold(CapabilityCard card)
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }
        try
        {
            var result = _scaffolder.Scaffold(ws, card.Descriptor.Id);
            StatusText = result.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"Scaffolding failed: {ex.Message}";
        }
    }
}

public partial class CapabilityGroup : ObservableObject
{
    public CapabilityGroup(string title) => Title = title;

    public string Title { get; }

    public ObservableCollection<CapabilityCard> Cards { get; } = [];
}

/// <summary>View wrapper for one capability descriptor.</summary>
public partial class CapabilityCard : ObservableObject
{
    public CapabilityCard(CapabilityDescriptor descriptor) => Descriptor = descriptor;

    public CapabilityDescriptor Descriptor { get; }

    public string Name => Descriptor.Name;
    public string Summary => Descriptor.Summary;
    public string Interface => Descriptor.Interface ?? "builder API";
    public string Permissions => Descriptor.Permissions.Count > 0
        ? string.Join(", ", Descriptor.Permissions)
        : "none required";
    public string DocsUrl => "https://docs.macro-deck.app/" + Descriptor.DocsPath;
}
