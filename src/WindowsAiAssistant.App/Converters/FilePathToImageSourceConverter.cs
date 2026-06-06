using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WindowsAiAssistant.App.Converters;

public sealed class FilePathToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var path = value?.ToString();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null!;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.UriSource = new Uri(path);
            return bitmap;
        }
        catch
        {
            return null!;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
