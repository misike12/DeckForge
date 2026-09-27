using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class IconStudioPage : Page
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
        if (e.PropertyName is nameof(IconStudioViewModel.Svg))
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
        var html = Wrap(_vm.Svg);
        try
        {
            await PreviewLarge.EnsureCoreWebView2Async();
            await PreviewSmall.EnsureCoreWebView2Async();
            PreviewLarge.NavigateToString(html);
            PreviewSmall.NavigateToString(html);
        }
        catch (Exception)
        {
            // Preview unavailable without the runtime; editing still works.
        }
    }

    private static string Wrap(string svg) =>
        "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\" />\n<style>\n" +
        "  html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; }\n" +
        "  svg { width: 100%; height: 100%; }\n" +
        "</style>\n</head>\n<body>" + svg + "</body>\n</html>";
}
