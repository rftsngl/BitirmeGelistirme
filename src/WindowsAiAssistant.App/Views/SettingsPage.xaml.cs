using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAiAssistant.App.ProviderSettings;

namespace WindowsAiAssistant.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<ProviderSettingsViewModel>();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    public ProviderSettingsViewModel ViewModel { get; }

    public IReadOnlyList<ModelProviderKind> ProviderKinds { get; } =
    [
        ModelProviderKind.OpenAICompatible,
        ModelProviderKind.Gemini,
        ModelProviderKind.Local
    ];

    public IReadOnlyList<ModelEndpointStyle> EndpointStyles { get; } =
    [
        ModelEndpointStyle.OpenAiChatCompletions,
        ModelEndpointStyle.GeminiGenerateContent
    ];

    public IReadOnlyList<ModelAuthScheme> AuthSchemes { get; } =
    [
        ModelAuthScheme.Bearer,
        ModelAuthScheme.Raw,
        ModelAuthScheme.None
    ];

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        SyncListSelectionToViewModel();
    }

    private void SyncListSelectionToViewModel()
    {
        if (ViewModel.SelectedProfile is null || ProfilesListBox.Items.Count == 0)
        {
            return;
        }

        foreach (var item in ProfilesListBox.Items)
        {
            if (item is ProviderSettings.ProfileListItem listItem &&
                listItem.Id.Equals(ViewModel.SelectedProfile.Id, StringComparison.OrdinalIgnoreCase))
            {
                ProfilesListBox.SelectedItem = item;
                return;
            }
        }
    }

    private void ProfilesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesListBox.SelectedItem is ProviderSettings.ProfileListItem listItem)
        {
            ViewModel.SelectedProfile = listItem.Profile;
        }

        ApiKeyPasswordBox.Password = string.Empty;
    }

    private void ApiKeyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.ApiKeyInput = ApiKeyPasswordBox.Password;
    }

    private async void SetActive_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SetActiveAsync().ConfigureAwait(true);
    }

    private async void SaveKey_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveKeyAsync().ConfigureAwait(true);
        ApiKeyPasswordBox.Password = string.Empty;
    }

    private async void RemoveKey_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RemoveKeyAsync().ConfigureAwait(true);
    }

    private async void TestConnection_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.TestConnectionAsync().ConfigureAwait(true);
    }

    private async void NewProfile_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginNewProfile();
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void PresetOpenAi_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginFromPreset("openai");
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void PresetGemini_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginFromPreset("gemini");
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void PresetOllama_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginFromPreset("ollama");
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void PresetLmStudio_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginFromPreset("lmstudio");
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void EditProfile_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BeginEditCurrent();
        if (!ViewModel.IsEditing)
        {
            return;
        }

        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void DeleteProfile_OnClick(object sender, RoutedEventArgs e)
    {
        var current = ViewModel.SelectedProfile;
        if (current is null)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Profili sil",
            Content = $"'{current.DisplayName}' profili silinsin mi? Kayıtlı API anahtarı varsa o da kaldırılır.",
            PrimaryButtonText = "Sil",
            CloseButtonText = "İptal",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        ViewModel.DeleteCurrent();
    }

    private async Task ShowProfileEditorAsync()
    {
        ProfileEditorDialog.XamlRoot = XamlRoot;
        ProfileEditorErrorBar.IsOpen = false;
        await ProfileEditorDialog.ShowAsync();
    }

    private void ProfileEditorDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var saveResult = ViewModel.SaveDraft();
        if (!saveResult.Success)
        {
            args.Cancel = true;
            ProfileEditorErrorBar.Message = saveResult.Message;
            ProfileEditorErrorBar.IsOpen = true;
            return;
        }

        SyncListSelectionToViewModel();
    }

    private void ProfileEditorDialog_OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.CancelEdit();
    }

    private void TryInAssistant_OnClick(object sender, RoutedEventArgs e) => ViewModel.NavigateToAssistant();
}
