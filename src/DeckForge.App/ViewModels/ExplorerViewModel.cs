using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class FileNode : ObservableObject
{
    public FileNode(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
    }

    public string FullPath { get; }
    public bool IsDirectory { get; }
    public string Name { get; }

    public string Glyph => IsDirectory ? "\uE8B7" : "\uE8A5";

    public ObservableCollection<FileNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;
}

public partial class ExplorerViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;

    public ExplorerViewModel(WorkspaceManager workspaces)
    {
        _workspaces = workspaces;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _rootPath = "";

    [ObservableProperty]
    private FileNode? _root;

    public void Load()
    {
        var ws = _workspaces.Current;
        HasWorkspace = ws is not null;
        if (ws is null)
        {
            Root = null;
            return;
        }

        RootPath = ws.RootDirectory;
        Root = BuildNode(new DirectoryInfo(ws.RootDirectory), depth: 0);
        if (Root is not null)
        {
            Root.IsExpanded = true;
        }
    }

    private static FileNode? BuildNode(DirectoryInfo dir, int depth)
    {
        var node = new FileNode(dir.FullName, isDirectory: true);
        try
        {
            foreach (var sub in dir.EnumerateDirectories())
            {
                if (sub.Name is "bin" or "obj" or ".git" or ".deckforge" or ".vs" or ".idea" || sub.Name.EndsWith(".Tests"))
                {
                    if (sub.Name is "bin" or "obj" or ".git" or ".deckforge" or ".vs" or ".idea")
                    {
                        continue;
                    }
                }
                if (depth < 6)
                {
                    var child = BuildNode(sub, depth + 1);
                    if (child is not null)
                    {
                        node.Children.Add(child);
                    }
                }
            }
            foreach (var file in dir.EnumerateFiles())
            {
                node.Children.Add(new FileNode(file.FullName, isDirectory: false));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // skip inaccessible dirs
        }
        return node;
    }
}
