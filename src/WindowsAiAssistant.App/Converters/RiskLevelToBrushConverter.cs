using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WindowsAiAssistant.App.Converters;

public sealed class RiskLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var risk = value?.ToString() ?? string.Empty;
        return risk.ToLowerInvariant() switch
        {
            "low" => new SolidColorBrush(Color.FromArgb(255, 0x10, 0x88, 0x47)),
            "medium" => new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            "high" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            "critical" => new SolidColorBrush(Color.FromArgb(255, 0x90, 0x10, 0x10)),
            _ => new SolidColorBrush(Color.FromArgb(255, 0x80, 0x80, 0x80))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
