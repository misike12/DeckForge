using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class IconStudioViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;

    public IconStudioViewModel(WorkspaceManager workspaces)
    {
        _workspaces = workspaces;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
        BuildTemplates();

        // Select before rendering. The constructor used to call RenderSelected with nothing
        // selected and return an empty string, so the page opened on a blank preview until the
        // user happened to click a template - and Save would then have written an empty <svg>.
        SelectedTemplate = Templates.FirstOrDefault();
        Svg = RenderSelected();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _svg = "";

    [ObservableProperty]
    private IconTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _shapeColor = "#4F8CFF";

    [ObservableProperty]
    private string _accentColor = "#F5A623";

    [ObservableProperty]
    private string _iconText = "";

    public ObservableCollection<IconTemplate> Templates { get; } = [];

    partial void OnSelectedTemplateChanged(IconTemplate? value) => Refresh();
    partial void OnShapeColorChanged(string value) => Refresh();
    partial void OnAccentColorChanged(string value) => Refresh();

    partial void OnIconTextChanged(string value) => Refresh();

    public void Load() => HasWorkspace = _workspaces.Current is not null;

    /// <summary>Shell hook.</summary>
    public void RefreshOnNavigate() => Load();

    private void BuildTemplates()
    {
        Templates.Add(new IconTemplate("Cloud", """
            <circle cx="24" cy="24" r="11" fill="$Accent" />
            <path d="M18 45h26a9.5 9.5 0 0 0 1-18.9A13.5 13.5 0 0 0 19 21.6 11.5 11.5 0 0 0 18 45z" fill="$Shape" />
            """));
        Templates.Add(new IconTemplate("Bolt", """
            <path d="M35 10 20 32h10l-4 20 16-25H31l6-17z" fill="$Shape" />
            """));
        Templates.Add(new IconTemplate("Wave", """
            <path d="M8 38c6-12 12-12 18 0s12 12 18 0" stroke="$Shape" stroke-width="6" fill="none" stroke-linecap="round" />
            <circle cx="47" cy="17" r="6" fill="$Accent" />
            """));
        Templates.Add(new IconTemplate("Tile grid", """
            <rect x="10" y="10" width="20" height="20" rx="5" fill="$Shape" />
            <rect x="34" y="10" width="20" height="20" rx="5" fill="$Accent" />
            <rect x="10" y="34" width="20" height="20" rx="5" fill="$Accent" />
            <rect x="34" y="34" width="20" height="20" rx="5" fill="$Shape" />
            """));
        Templates.Add(new IconTemplate("Initial", """
            <rect x="8" y="8" width="48" height="48" rx="12" fill="$Shape" />
            <text x="32" y="41" font-family="Segoe UI, sans-serif" font-size="26" font-weight="600"
                  fill="#FFFFFF" text-anchor="middle">$Text</text>
            """));
    }

    private void Refresh()
    {
        if (SelectedTemplate is null && Templates.Count > 0)
        {
            SelectedTemplate = Templates[0];
            return;
        }
        Svg = RenderSelected();
    }

    private string RenderSelected()
    {
        if (SelectedTemplate is null)
        {
            return "";
        }
        return SelectedTemplate.Shape
            .Replace("$Shape", SafeColor(ShapeColor))
            .Replace("$Accent", SafeColor(AccentColor))
            .Replace("$Text", System.Security.SecurityElement.Escape(IconText is "" ? "D" : IconText));
    }

    [RelayCommand]
    private void SaveToWorkspace()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a workspace first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Svg))
        {
            // There is no template selected, or the selected one rendered nothing. Writing
            // <svg></svg> over the plugin's icon would replace a working icon with an empty
            // document, and the host would fail to load it at run time with nothing pointing here.
            StatusText = "Nothing to save - pick a template first.";
            return;
        }

        var iconPath = Path.Combine(ws.AssetsDirectory, "icon.svg");
        var existed = File.Exists(iconPath);
        Directory.CreateDirectory(ws.AssetsDirectory);
        File.WriteAllText(iconPath, Wrap());
        StatusText = existed ? $"Replaced {iconPath}" : $"Saved {iconPath}";
    }

    [RelayCommand]
    private void SaveAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "SVG|*.svg", FileName = "icon.svg" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        File.WriteAllText(dialog.FileName, Wrap());
        StatusText = $"Saved {dialog.FileName}";
    }

    private string Wrap() => $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
        {Svg}
        </svg>
        """;

    /// <summary>
    /// A six-digit hex colour, or the accent default.
    /// </summary>
    /// <remarks>
    /// This runs on every keystroke of the colour boxes - twice, since two colours are rendered -
    /// and used <c>Regex.IsMatch</c> with a pattern that is not constant, so the regex engine was
    /// re-parsing the pattern on each call. A GeneratedRegex is compiled once.
    /// </remarks>
    private static string SafeColor(string color) =>
        HexColor().IsMatch(color) ? color : "#4F8CFF";

    [System.Text.RegularExpressions.GeneratedRegex(@"^#[0-9A-Fa-f]{6}$")]
    private static partial System.Text.RegularExpressions.Regex HexColor();
}

public sealed record IconTemplate(string Name, string Shape);
