using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VoiceGarden.UI.Views;

public partial class SherpaModelsView : UserControl
{
    public SherpaModelsView()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }
}
