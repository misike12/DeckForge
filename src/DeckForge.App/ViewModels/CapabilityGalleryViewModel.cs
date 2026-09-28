using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.Core.Capabilities;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;
public partial class CapabilityGalleryViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;

    public CapabilityGalleryViewModel(WorkspaceManager workspaces)
    {
        _workspaces = workspaces;
        Services.ShellMessenger.WorkspaceChanged += _ => RefreshWorkspaceState();
        RefreshWorkspaceState();
        BuildGroups();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// Capability ids the open plugin already has, so a card can say so instead of offering an
    /// "Add to plugin" that silently does nothing.
    /// </summary>
    public HashSet<string> ScaffoldsPresent { get; private set; } = new(StringComparer.Ordinal);

    public ObservableCollection<CapabilityGroup> Groups { get; } = [];

    private void RefreshWorkspaceState()
    {
        HasWorkspace = _workspaces.Current is not null;
        ScaffoldsPresent = _workspaces.Current is { } ws
            ? CodeGen.Capabilities.CapabilityScaffolder.ScaffoldsPresent(ws).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private void BuildGroups()
    {
        Groups.Clear();
        foreach (var category in Enum.GetValues<CapabilityCategory>())
        {
            var group = new CapabilityGroup(GroupTitle(category));
            foreach (var capability in CapabilityCatalog.All.Where(c => c.Category == category))
            {
                group.Cards.Add(new CapabilityCard(capability));
            }

            Groups.Add(group);
        }
    }

    /// <summary>
    /// A readable heading for a category.
    /// </summary>
    /// <remarks>
    /// This used to be category.ToString().Replace("And", " &amp; "), which turned a future
    /// Android value into &amp;roid and Sandboxes into S&amp;boxes - it substituted the first three
    /// letters of any word containing it. Only the enum separator was replaced; the words are now
    /// spelled out, so a new category cannot produce a typo.
    /// </remarks>
    private static string GroupTitle(CapabilityCategory category) => category switch
    {
        CapabilityCategory.Buttons => "Buttons",
        CapabilityCategory.Data => "Data",
        CapabilityCategory.DeckAndClients => "Deck and clients",
        CapabilityCategory.SetupAndMaintenance => "Setup and maintenance",
        CapabilityCategory.HardwareAndSurfaces => "Hardware and surfaces",
        _ => category.ToString(),
    };

    [RelayCommand]
    private void OpenDocs(CapabilityCard card)
    {
        var path = card.Descriptor.DocsPath;
        Services.ShellMessenger.NavigateTo(
            string.IsNullOrWhiteSpace(path) ? "docs" : $"docs::{path}");
    }

    /// <summary>Adds the capability to the open plugin.</summary>
    /// <remarks>
    /// This called a stale App-local <c>CapabilityScaffolder</c> that implemented two of the
    /// twenty-three capabilities and answered "not implemented yet" for the rest - a second,
    /// parallel scaffolder beside the real one in CodeGen, with its own copy of the source-patcher
    /// and of the two that anchored on <c>IndexOf("using ")</c> and so could insert a using
    /// directive inside a comment. The local one is gone; this is the same scaffolder the test
    /// suite proves compiles all twenty-three.
    /// </remarks>
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
            var result = CodeGen.Capabilities.CapabilityScaffolder.Scaffold(ws, card.Descriptor.Id);
            StatusText = result.Message;
            RefreshWorkspaceState();
            ScaffoldsPresent = CodeGen.Capabilities.CapabilityScaffolder
                .ScaffoldsPresent(ws)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

    /// <summary>The descriptor's own glyph, so each card is distinguishable.</summary>
    public string Glyph => Descriptor.Glyph;

    public string DocsUrl => "https://docs.macro-deck.app/" + Descriptor.DocsPath;
}