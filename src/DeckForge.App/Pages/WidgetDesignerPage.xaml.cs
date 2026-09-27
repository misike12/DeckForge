using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class WidgetDesignerPage : Page, IRefreshOnNavigate
{
    /// <summary>Exposed for XAML ItemsSource bindings inside data templates.</summary>
    public static System.Collections.Generic.IReadOnlyList<string> EventNames => NodeEvent.EventNames;



    /// <summary>Exposed for XAML ItemsSource bindings inside data templates.</summary>
    public static System.Collections.Generic.IReadOnlyList<string> SchemaTypes => SchemaProperty.SchemaTypes;

    private readonly WidgetDesignerViewModel _vm;
    private bool _initialized;

    public WidgetDesignerPage(WidgetDesignerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        foreach (var type in WidgetDesignerViewModel.NodeTypes)
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Content = type.DisplayName,
                ToolTip = type.Summary,
                Tag = type.WireType,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 5, 10, 5),
            };
            button.Click += Palette_Click;
            PaletteList.Items.Add(button);
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WidgetDesignerViewModel.PreviewHtml))
            {
                _ = RenderPreviewAsync();
            }
        };

        Loaded += async (_, _) =>
        {
            _initialized = true;
            vm.Load();
            await RenderPreviewAsync();
        };
    }

    public void RefreshOnNavigate() => _vm.Load();

    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string type })
        {
            _vm.AddNodeCommand.Execute(type);
        }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is WidgetNode node)
        {
            _vm.SelectedNode = node;
        }
    }

    private void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WidgetNode node })
        {
            _vm.SelectedNode = node;
            _vm.RemoveNodeCommand.Execute(null);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WidgetNode node })
        {
            _vm.SelectedNode = node;
            _vm.MoveUpCommand.Execute(null);
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WidgetNode node })
        {
            _vm.SelectedNode = node;
            _vm.MoveDownCommand.Execute(null);
        }
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::ui/views/widget-types");

    private async System.Threading.Tasks.Task RenderPreviewAsync()
    {
        if (!_initialized)
        {
            return;
        }
        try
        {
            await Preview.EnsureCoreWebView2Async();
            Preview.NavigateToString(_vm.PreviewHtml);
        }
        catch (Exception)
        {
            // WebView2 runtime missing; designer still works.
        }
    }
}
