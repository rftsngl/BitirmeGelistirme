using System.Collections.ObjectModel;
using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class SpeechModelGroupViewModel : ObservableObject
{
    public SpeechModelGroupViewModel(string title, string description, IEnumerable<SpeechModelDownloadItemViewModel> items)
    {
        Title = title;
        Description = description;
        Items = new ObservableCollection<SpeechModelDownloadItemViewModel>(items);
    }

    public string Title { get; }

    public string Description { get; }

    public ObservableCollection<SpeechModelDownloadItemViewModel> Items { get; }

    public int DownloadedCount => Items.Count(item => item.IsDownloaded);

    public int TotalCount => Items.Count;

    public string Summary => $"{DownloadedCount}/{TotalCount} hazır";
}
