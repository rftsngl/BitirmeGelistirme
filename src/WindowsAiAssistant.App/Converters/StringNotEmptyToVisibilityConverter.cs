using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace WindowsAiAssistant.App.Converters;

public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasValue = !string.IsNullOrWhiteSpace(value as string);
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
