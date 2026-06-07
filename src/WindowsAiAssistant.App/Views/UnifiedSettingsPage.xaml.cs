using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAiAssistant.App.ProviderSettings;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.ViewModels;

namespace WindowsAiAssistant.App.Views;

public sealed partial class UnifiedSettingsPage : Page
{
    public UnifiedSettingsPage()
    {
        InitializeComponent();
        ProviderVM = App.Services.GetRequiredService<ProviderSettingsViewModel>();
        AppVM = App.Services.GetRequiredService<AppSettingsViewModel>();
        DataContext = ProviderVM;
        Loaded += OnLoaded;
    }

    public ProviderSettingsViewModel ProviderVM { get; }
    public AppSettingsViewModel AppVM { get; }

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
        ProviderVM.Load();
        SyncListSelectionToViewModel();
        // Bind AppVM sections
        AudioSection.DataContext = AppVM;
        SpeechModelsSection.DataContext = AppVM;
        SecuritySection.DataContext = AppVM;
        DeveloperSection.DataContext = AppVM;
        AppVM.RefreshSpeechDiagnostics();
        AppVM.RefreshSpeechModelInventory();
    }

    private void SyncListSelectionToViewModel()
    {
        if (ProviderVM.SelectedProfile is null || ProfilesListBox.Items.Count == 0) return;
        foreach (var item in ProfilesListBox.Items)
        {
            if (item is ProfileListItem listItem &&
                listItem.Id.Equals(ProviderVM.SelectedProfile.Id, StringComparison.OrdinalIgnoreCase))
            {
                ProfilesListBox.SelectedItem = item;
                return;
            }
        }
    }

    private void ProfilesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesListBox.SelectedItem is ProfileListItem listItem)
            ProviderVM.SelectedProfile = listItem.Profile;
        ApiKeyPasswordBox.Password = string.Empty;
    }

    private void ApiKeyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        => ProviderVM.ApiKeyInput = ApiKeyPasswordBox.Password;

    private async void SetActive_OnClick(object sender, RoutedEventArgs e)
        => await ProviderVM.SetActiveAsync().ConfigureAwait(true);

    private async void SaveKey_OnClick(object sender, RoutedEventArgs e)
    {
        await ProviderVM.SaveKeyAsync().ConfigureAwait(true);
        ApiKeyPasswordBox.Password = string.Empty;
    }

    private async void RemoveKey_OnClick(object sender, RoutedEventArgs e)
        => await ProviderVM.RemoveKeyAsync().ConfigureAwait(true);

    private async void TestConnection_OnClick(object sender, RoutedEventArgs e)
        => await ProviderVM.TestConnectionAsync().ConfigureAwait(true);

    private async void NewProfile_OnClick(object sender, RoutedEventArgs e)
    {
        ProviderVM.BeginNewProfile();
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void PresetOpenAi_OnClick(object sender, RoutedEventArgs e)
    {
        ProviderVM.BeginFromPreset("openai");
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void PresetGemini_OnClick(object sender, RoutedEventArgs e)
    {
        ProviderVM.BeginFromPreset("gemini");
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void PresetOllama_OnClick(object sender, RoutedEventArgs e)
    {
        ProviderVM.BeginFromPreset("ollama");
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void PresetLmStudio_OnClick(object sender, RoutedEventArgs e)
    {
        ProviderVM.BeginFromPreset("lmstudio");
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void EditProfile_OnClick(object sender, RoutedEventArgs e)
    {
        ProviderVM.BeginEditCurrent();
        if (!ProviderVM.IsEditing) return;
        await ShowProfileEditorAsync().ConfigureAwait(true);
    }

    private async void DeleteProfile_OnClick(object sender, RoutedEventArgs e)
    {
        var current = ProviderVM.SelectedProfile;
        if (current is null) return;
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
        if (result != ContentDialogResult.Primary) return;
        ProviderVM.DeleteCurrent();
    }

    private async Task ShowProfileEditorAsync()
    {
        ProfileEditorDialog.XamlRoot = XamlRoot;
        ProfileEditorErrorBar.IsOpen = false;
        await ProfileEditorDialog.ShowAsync();
    }

    private void ProfileEditorDialog_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var saveResult = ProviderVM.SaveDraft();
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
        => ProviderVM.CancelEdit();

    private void TryInAssistant_OnClick(object sender, RoutedEventArgs e)
        => ProviderVM.NavigateToAssistant();

    private void OpenCapabilities_OnClick(object sender, RoutedEventArgs e)
        => App.Services.GetRequiredService<INavigationService>().Navigate(typeof(CapabilitiesPage));

    private void ShowModelSection_OnClick(object sender, RoutedEventArgs e)
        => ExpandOnly(ModelSection);

    private void ShowAudioSection_OnClick(object sender, RoutedEventArgs e)
        => ExpandOnly(AudioSection);

    private void ShowSecuritySection_OnClick(object sender, RoutedEventArgs e)
        => ExpandOnly(SecuritySection);

    private void ShowDeveloperSection_OnClick(object sender, RoutedEventArgs e)
        => ExpandOnly(DeveloperSection);

    private void ExpandOnly(Expander target)
    {
        ModelSection.IsExpanded = target == ModelSection;
        AudioSection.IsExpanded = target == AudioSection;
        SecuritySection.IsExpanded = target == SecuritySection;
        DeveloperSection.IsExpanded = target == DeveloperSection;
        SettingsScrollViewer.ChangeView(null, 0, null, false);
    }

    private async void SaveAppSettings_OnClick(object sender, RoutedEventArgs e)
        => await AppVM.SaveAsync().ConfigureAwait(true);
}
