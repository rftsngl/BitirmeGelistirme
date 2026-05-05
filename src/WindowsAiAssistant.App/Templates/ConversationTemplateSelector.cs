using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAiAssistant.App.Models;

namespace WindowsAiAssistant.App.Templates;

public sealed class ConversationTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserMessageTemplate { get; set; }
    public DataTemplate? AssistantStatusTemplate { get; set; }
    public DataTemplate? PendingApprovalTemplate { get; set; }
    public DataTemplate? ResultCardTemplate { get; set; }
    public DataTemplate? ErrorCardTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item switch
        {
            ConversationItem { Kind: ConversationItemKind.UserMessage } => UserMessageTemplate,
            ConversationItem { Kind: ConversationItemKind.AssistantStatus } => AssistantStatusTemplate,
            ConversationItem { Kind: ConversationItemKind.PendingApproval } => PendingApprovalTemplate,
            ConversationItem { Kind: ConversationItemKind.ResultCard } => ResultCardTemplate,
            ConversationItem { Kind: ConversationItemKind.ErrorCard } => ErrorCardTemplate,
            _ => null
        };
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
