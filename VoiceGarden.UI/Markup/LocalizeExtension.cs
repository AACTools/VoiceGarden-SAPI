using Avalonia.Data;
using Avalonia.Markup.Xaml;
using VoiceGarden.UI.Localization;

namespace VoiceGarden.UI.Markup;

/// <summary>
/// Avalonia XAML markup extension for localized strings.
/// Usage: {lang:Localize KeyName}
/// or with format args: {lang:Localize KeyName, Args={x:Static someValue}}
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocalizeExtension() { }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Loc.GetString(Key);
    }
}
