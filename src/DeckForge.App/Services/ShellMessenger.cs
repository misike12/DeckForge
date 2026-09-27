using System.Windows;

namespace DeckForge.App.Services;

/// <summary>
/// Minimal event bus so pages can ask the shell to navigate (e.g. Home -> New Plugin),
/// or broadcast workspace changes, without referencing the window.
/// </summary>
public static class ShellMessenger
{
    public static event Action<string>? NavigationRequested;

    /// <summary>Raised when the open workspace changed (args = solution path or null).</summary>
    public static event Action<string?>? WorkspaceChanged;

    public static void NavigateTo(string tag) => NavigationRequested?.Invoke(tag);

    public static void NotifyWorkspaceChanged(string? solutionPath) => WorkspaceChanged?.Invoke(solutionPath);

    /// <summary>Call from MainWindow to start serving requests.</summary>
    public static void Attach(Window window)
    {
        NavigationRequested += tag => window.Dispatcher.Invoke(() =>
        {
            if (window is MainWindow main)
            {
                main.NavigateTo(tag);
            }
        });
    }
}
