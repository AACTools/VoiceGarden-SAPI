using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VoiceGarden.UI.ViewModels;

namespace VoiceGarden.UI.Views;

public partial class SherpaModelsView : UserControl
{
    public SherpaModelsView()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }

    private void PreviewModel_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelId && DataContext is SherpaModelsViewModel vm)
        {
            var model = vm.AllModels.FirstOrDefault(m => m.Id == modelId);
            if (model != null)
                vm.PreviewModelCommand.Execute(model);
        }
    }
}
