using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WindowsAiAssistant.App.Converters;

public sealed class BoolToSuccessBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var success = value is bool b && b;
        return success
            ? new SolidColorBrush(Color.FromArgb(255, 0x10, 0x88, 0x47))
            : new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
