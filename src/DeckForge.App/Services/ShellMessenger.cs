using System.Windows;
using System.Windows.Threading;

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

    /// <summary>
    /// Raised when something wants the stub host started. F5, and anything else that needs to run
    /// the plugin without going through the Build &amp; Run page's own button.
    /// </summary>
    /// <remarks>
    /// F5 is documented in three places as running the stub host and only navigated to the page,
    /// so following the documentation did nothing. The window raises this rather than reaching into
    /// the Build &amp; Run view model, which keeps the shell unaware of any particular page.
    /// </remarks>
    public static event Action? RunRequested;

    /// <summary>Raised when the start-of-session check found a newer release.</summary>
    public static event Action<UpdateCheckResult>? UpdateAvailable;

    /// <summary>
    /// Raised when an exception on the UI thread was handled and the app carried on.
    /// </summary>
    /// <remarks>
    /// The handler swallows the first three so a misbehaving page does not close the app, but it
    /// used to do so in complete silence - the only record was a file the user had not opened. The
    /// shell shows these, so surviving a crash is visible rather than mysterious.
    /// </remarks>
    public static event Action<Exception>? Unhandled;

    public static void ReportUnhandled(Exception exception) => Unhandled?.Invoke(exception);

    public static void NavigateTo(string tag) => NavigationRequested?.Invoke(tag);

    public static void RequestRun() => RunRequested?.Invoke();

    public static void AnnounceUpdate(UpdateCheckResult result) => UpdateAvailable?.Invoke(result);

    public static void NotifyWorkspaceChanged(string? solutionPath) => WorkspaceChanged?.Invoke(solutionPath);

    private static Window? _window;

    /// <summary>Call from MainWindow to start serving requests.</summary>
    /// <remarks>
    /// The handler is stored rather than added with <c>+=</c> on a lambda, so detaching on window
    /// close is possible. It was not, which meant every MainWindow created in a session - and
    /// every test that built one - left a live handler holding it alive.
    /// <para>
    /// The request is posted rather than invoked. <c>Dispatcher.Invoke</c> blocks the caller until
    /// the UI thread runs the navigation, and it deadlocks when the caller is already on the UI
    /// thread during a dispatcher operation. A page asking to navigate is on the UI thread, so
    /// that was the normal case rather than an edge case.
    /// </para>
    /// </remarks>
    public static void Attach(Window window)
    {
        Detach();
        _window = window;
        window.Closed += OnWindowClosed;
        NavigationRequested += OnNavigationRequested;
    }

    /// <summary>Stops serving requests and releases the window.</summary>
    public static void Detach()
    {
        if (_window is not null)
        {
            NavigationRequested -= OnNavigationRequested;
            _window.Closed -= OnWindowClosed;
            _window = null;
        }
    }

    private static void OnWindowClosed(object? sender, EventArgs e) => Detach();

    private static void OnNavigationRequested(string tag)
    {
        if (_window is not MainWindow main)
        {
            return;
        }

        var dispatcher = main.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            main.NavigateTo(tag);
            return;
        }

        dispatcher.BeginInvoke(() => main.NavigateTo(tag));
    }
}
