using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAiAssistant.App.ViewModels;

namespace WindowsAiAssistant.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    public HistoryViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void DeleteSelected_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Kaydı sil",
            Content = "Seçili geçmiş kaydı kalıcı olarak silinsin mi? İlişkili ekran görüntüsü varsa o da kaldırılır.",
            PrimaryButtonText = "Sil",
            CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.DeleteSelectedAsync().ConfigureAwait(true);
    }

    private async void ClearAll_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Tüm geçmişi temizle",
            Content = $"Toplam {ViewModel.TotalRunCount} kayıt silinecek. Bu işlem geri alınamaz. Devam etmek istiyor musunuz?",
            PrimaryButtonText = "Tümünü sil",
            CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.ClearAllAsync().ConfigureAwait(true);
    }
}
