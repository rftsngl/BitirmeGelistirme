using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WindowsAiAssistant.App.Audio;

namespace WindowsAiAssistant.App.Converters;

public sealed class SpeechModelStateToBackgroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not SpeechModelDownloadState state)
        {
            return new SolidColorBrush(Color.FromArgb(40, 0x80, 0x80, 0x80));
        }

        return state switch
        {
            SpeechModelDownloadState.Downloaded =>
                new SolidColorBrush(Color.FromArgb(48, 0x10, 0x88, 0x47)),
            SpeechModelDownloadState.Downloading =>
                new SolidColorBrush(Color.FromArgb(48, 0xCA, 0x80, 0x00)),
            SpeechModelDownloadState.Failed =>
                new SolidColorBrush(Color.FromArgb(48, 0xC4, 0x2B, 0x1C)),
            _ => new SolidColorBrush(Color.FromArgb(48, 0x80, 0x80, 0x80))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class SpeechModelStateToForegroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not SpeechModelDownloadState state)
        {
            return new SolidColorBrush(Color.FromArgb(255, 0xA0, 0xA0, 0xA0));
        }

        return state switch
        {
            SpeechModelDownloadState.Downloaded =>
                new SolidColorBrush(Color.FromArgb(255, 0x10, 0x88, 0x47)),
            SpeechModelDownloadState.Downloading =>
                new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            SpeechModelDownloadState.Failed =>
                new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            _ => new SolidColorBrush(Color.FromArgb(255, 0xA0, 0xA0, 0xA0))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
