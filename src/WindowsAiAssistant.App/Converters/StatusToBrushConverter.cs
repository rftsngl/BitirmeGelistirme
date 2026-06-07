using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WindowsAiAssistant.App.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value?.ToString() ?? string.Empty;
        return status.ToUpperInvariant() switch
        {
            "COMPLETED" => new SolidColorBrush(Color.FromArgb(255, 0x10, 0x88, 0x47)),
            "PENDINGAPPROVAL" => new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            "BEKLİYOR" => new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            "ONAYLANDI" => new SolidColorBrush(Color.FromArgb(255, 0x10, 0x88, 0x47)),
            "REDDEDİLDİ" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            "BLOCKED" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            "ERROR" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            "FAILED" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            "TAMAMLANDI" => new SolidColorBrush(Color.FromArgb(255, 0x10, 0x88, 0x47)),
            "BAŞARILI" => new SolidColorBrush(Color.FromArgb(255, 0x10, 0x88, 0x47)),
            "İNDİRİLDİ" => new SolidColorBrush(Color.FromArgb(255, 0x10, 0x88, 0x47)),
            "İNDİRİLİYOR" => new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            "İNDİRİLMEDİ" => new SolidColorBrush(Color.FromArgb(255, 0x8A, 0x8A, 0x8A)),
            "HATA" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            "BAŞARISIZ" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            "ONAY" => new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            "LİMİT" => new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            "LIMIT" => new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            "ONAY REDDEDİLDİ" => new SolidColorBrush(Color.FromArgb(255, 0xCA, 0x80, 0x00)),
            "GATE ENGELİ" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            "SON ADIM BAŞARISIZ" => new SolidColorBrush(Color.FromArgb(255, 0xC4, 0x2B, 0x1C)),
            _ => new SolidColorBrush(Color.FromArgb(255, 0x60, 0x60, 0x60))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
