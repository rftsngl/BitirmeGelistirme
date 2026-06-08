using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WindowsAiAssistant.App.Overlay;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.ViewModels;

namespace WindowsAiAssistant.App.Views;

public sealed partial class AssistantPage : Page
{
    public AssistantPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AssistantViewModel>();
        DataContext = ViewModel;
        ViewModel.Conversation.CollectionChanged += OnConversationChanged;
        ViewModel.ScrollToEndRequested += ScrollConversationToEnd;
        Loaded += (_, _) => CommandInputTextBox.Focus(FocusState.Programmatic);
        Unloaded += (_, _) =>
        {
            ViewModel.Conversation.CollectionChanged -= OnConversationChanged;
            ViewModel.ScrollToEndRequested -= ScrollConversationToEnd;
        };
    }

    public AssistantViewModel ViewModel { get; }

    private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        ScrollConversationToEnd();

    private void ScrollConversationToEnd()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ConversationScrollViewer.UpdateLayout();
            ConversationScrollViewer.ChangeView(null, ConversationScrollViewer.ScrollableHeight, null, false);
        });
    }

    private void CommandInputTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlPressed = IsKeyDown(VirtualKey.Control);
        var shiftPressed = IsKeyDown(VirtualKey.Shift);

        if (e.Key == VirtualKey.Escape && ViewModel.CancelRunCommand.CanExecute(null))
        {
            ViewModel.CancelRunCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrlPressed && e.Key is VirtualKey.K or VirtualKey.L)
        {
            CommandInputTextBox.Focus(FocusState.Programmatic);
            CommandInputTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Enter || shiftPressed)
        {
            return;
        }

        e.Handled = true;
        TrySubmitFromComposer();
    }

    private void TrySubmitFromComposer()
    {
        var text = CommandInputTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || ViewModel.IsBusy)
        {
            return;
        }

        ViewModel.CommandInput = text;
        if (ViewModel.SubmitCommand.CanExecute(null))
        {
            ViewModel.SubmitCommand.Execute(null);
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
         & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string chipText })
        {
            ViewModel.CommandInput = chipText;
            CommandInputTextBox.Focus(FocusState.Programmatic);
        }
    }

    private void VoiceButton_Click(object sender, RoutedEventArgs e) =>
        SafeFireAndForget.Run(async () =>
        {
            var overlay = App.Services.GetRequiredService<AssistantOverlayWindow>();
            await overlay.RunVoiceSessionAsync().ConfigureAwait(true);
        }, nameof(VoiceButton_Click));
}
