using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VoiceGarden.UI.ViewModels;

namespace VoiceGarden.UI.Views;

public partial class VoiceConfigView : UserControl
{
    public VoiceConfigView()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);
    }

    private void PreviewVoice_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string voiceId && DataContext is VoiceConfigViewModel vm)
        {
            var voice = vm.AllVoices.FirstOrDefault(v => v.Id == voiceId);
            if (voice != null)
                vm.PreviewVoiceCommand.Execute(voice);
        }
    }
}
