using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
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
        Unloaded += (_, _) => ViewModel.Conversation.CollectionChanged -= OnConversationChanged;
    }

    public AssistantViewModel ViewModel { get; }

    private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ConversationScrollViewer.UpdateLayout();
            ConversationScrollViewer.ChangeView(null, ConversationScrollViewer.ScrollableHeight, null, false);
        });
    }

    private void CommandInputTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var ctrlPressed = (Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (!ctrlPressed)
        {
            return;
        }

        if (ViewModel.SubmitCommand.CanExecute(null))
        {
            ViewModel.SubmitCommand.Execute(null);
            e.Handled = true;
        }
    }
}
