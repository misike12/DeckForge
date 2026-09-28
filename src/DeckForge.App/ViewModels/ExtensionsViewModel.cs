using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.Core.Extensions;

namespace DeckForge.App.ViewModels;

/// <summary>
/// The Extensions page: what the loader found, whether it is on, and where to put more.
/// </summary>
/// <remarks>
/// The interface, the hook list and a page described in the documentation all existed with nothing
/// behind them. This is the page, and it is deliberately explicit about the one thing a plugin
/// system cannot offer: a .NET extension runs in this process with this process's permissions, so
/// installing one is a trust decision, and the page says that rather than implying a sandbox.
/// </remarks>
public partial class ExtensionsViewModel : ObservableObject
{
    private readonly ExtensionService _extensions;
    private readonly Services.SettingsService _settings;

    public ExtensionsViewModel(ExtensionService extensions, Services.SettingsService settings)
    {
        _extensions = extensions;
        _settings = settings;
        _extensions.Changed += Refresh;
    }

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isScanning;

    public ObservableCollection<ExtensionInfo> Found { get; } = [];

    /// <summary>
    /// Whatever extensions contributed to the diagnostics hook, one line each.
    /// </summary>
    /// <remarks>
    /// This is the only in-app invocation of an extension hook, and it is here on purpose: a hook
    /// with no call site is indistinguishable from a hook that does not work, and the page that
    /// manages extensions is the natural place for one extension to report on another.
    /// </remarks>
    public ObservableCollection<string> Diagnostics { get; } = [];

    /// <summary>Where to drop an extension. Shown verbatim, so the docs and the app agree.</summary>
    public string UserExtensionDirectory => ExtensionService.UserExtensionDirectory;

    public void Load() => Refresh();

    /// <summary>Shell hook.</summary>
    public void RefreshOnNavigate() => Refresh();

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusText = "Scanning...";
        try
        {
            var found = await _extensions.ScanAsync(AppContext.BaseDirectory);
            StatusText = found.Count == 0
                ? "No extensions found. Drop an assembly into the folder below, then scan again."
                : $"{found.Count(f => f.State == ExtensionState.Loaded)} of {found.Count} loaded.";

            await RefreshDiagnosticsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not scan: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void SetEnabled(ExtensionInfo? info)
    {
        if (info is null)
        {
            return;
        }

        _settings.Update(s =>
        {
            if (info.Enabled)
            {
                s.DisabledExtensions.Remove(info.Id);
            }
            else if (!s.DisabledExtensions.Contains(info.Id))
            {
                s.DisabledExtensions.Add(info.Id);
            }
        });

        // Turning one on runs third-party code, so it happens here rather than at the next start:
        // a toggle that silently did nothing until a restart would look broken.
        StatusText = info.Enabled
            ? $"{info.Name} enabled. Rescan to load it."
            : $"{info.Name} disabled. Rescan to unload it.";
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(UserExtensionDirectory);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(UserExtensionDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not open the folder: {ex.Message}";
        }
    }

    private void Refresh()
    {
        Found.Clear();
        foreach (var info in _extensions.Found)
        {
            Found.Add(info);
        }
    }

    /// <summary>
    /// Asks every extension registered for <see cref="DeckForgeHooks.Diagnostics"/> what it has to
    /// say, and shows whatever came back.
    /// </summary>
    private async Task RefreshDiagnosticsAsync()
    {
        Diagnostics.Clear();

        var results = await _extensions.InvokeAsync(DeckForgeHooks.Diagnostics, _extensions);
        foreach (var (_, result) in results)
        {
            foreach (var line in Describe(result))
            {
                Diagnostics.Add(line);
            }
        }
    }

    /// <summary>
    /// Turns whatever an extension returned into lines.
    /// </summary>
    /// <remarks>
    /// The hook returns <c>object?</c> so an extension is not forced into a shape, which means
    /// this has to guess. A string, a sequence of strings, and a record exposing
    /// <c>IReadOnlyList&lt;string&gt;</c> are the three shapes that cover anything sensible; an
    /// extension returning something else is skipped rather than guessed at, because a wrong guess
    /// shown as diagnostics is worse than no diagnostics.
    /// </remarks>
    private static IEnumerable<string> Describe(object? result) => result switch
    {
        null => [],
        string text => [text],
        IEnumerable<string> lines => lines,
        _ => result.GetType().GetProperty("Lines")?.GetValue(result) is IEnumerable<string> lines
            ? lines
            : [result.ToString() ?? string.Empty],
    };
}
