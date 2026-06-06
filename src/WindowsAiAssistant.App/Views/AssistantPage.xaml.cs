using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WindowsAiAssistant.App.Overlay;
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

    private void CommandInputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlPressed = (Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        var shiftPressed = (Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

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

        if (ViewModel.SubmitCommand.CanExecute(null))
        {
            ViewModel.SubmitCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string chipText })
        {
            ViewModel.CommandInput = chipText;
            CommandInputTextBox.Focus(FocusState.Programmatic);
        }
    }

    private async void VoiceButton_Click(object sender, RoutedEventArgs e)
    {
        var overlay = App.Services.GetRequiredService<AssistantOverlayWindow>();
        await overlay.RunVoiceSessionAsync().ConfigureAwait(true);
    }
}
