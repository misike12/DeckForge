using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class IconStudioPage : Page, IRefreshOnNavigate
{
    private readonly IconStudioViewModel _vm;
    private bool _initialized;

    public IconStudioPage(IconStudioViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += async (_, _) =>
        {
            _initialized = true;
            await RenderPreviewsAsync();
        };
    }

    /// <summary>Shell hook.</summary>
    public void RefreshOnNavigate() => _vm.Load();

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::features/button-icons");

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any of these three changes the document. Watching Svg alone was enough for correctness only
        // by accident: Refresh reassigns Svg on every one of them, but Svg is also assigned in the
        // constructor before this page exists, so the first render has to be driven by Loaded.
        if (e.PropertyName is nameof(IconStudioViewModel.Svg)
            or nameof(IconStudioViewModel.ShapeColor)
            or nameof(IconStudioViewModel.AccentColor)
            or nameof(IconStudioViewModel.IconText))
        {
            _ = RenderPreviewsAsync();
        }
    }

    private async System.Threading.Tasks.Task RenderPreviewsAsync()
    {
        if (!_initialized)
        {
            return;
        }

        // The document, not the bare shape list: the templates hold only the inner elements, and
        // handing those to a browser produced two blank tiles.
        var html = PreviewHtml.Wrap(_vm.SvgDocument);
        try
        {
            await PreviewLarge.EnsureCoreWebView2Async();
            await PreviewSmall.EnsureCoreWebView2Async();
            PreviewLarge.NavigateToString(html);
            PreviewSmall.NavigateToString(html);
            _vm.StatusText = "";
        }
        catch (Exception ex)
        {
            // Reported rather than swallowed. The catch used to discard the exception, so a preview
            // that never rendered looked identical to one that had nothing to show - the page just
            // showed two empty tiles and gave no hint why. Editing still works without the runtime;
            // it is the preview that is unavailable, and the message says so.
            _vm.StatusText = $"Live preview unavailable: {ex.GetType().Name}: {ex.Message}. The SVG is still saved correctly.";
        }
    }
}
