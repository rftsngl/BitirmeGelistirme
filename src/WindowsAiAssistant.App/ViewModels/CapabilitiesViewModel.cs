using System.Collections.ObjectModel;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class CapabilitiesViewModel : ObservableObject
{
    private readonly List<CapabilityViewItem> _allItems =
    [
        NewItem("OpenApp", "\uE7F4", "Uygulama acma eylemi.", "Application"),
        NewItem("OpenFile", "\uE8E5", "Dosyayi varsayilan uygulamada acma eylemi.", "File"),
        NewItem("OpenUrl", "\uE774", "URL acma eylemi.", "Url"),
        NewItem("TypeText", "\uE765", "Aktif uygulamaya metin yazma eylemi.", "ForegroundWindow"),
        NewItem("PressKey", "\uE92E", "Tek tus gonderme eylemi.", "ForegroundWindow"),
        NewItem("PressShortcut", "\uE92E", "Klavye kisayolu gonderme eylemi.", "ForegroundWindow"),
        NewItem("Wait", "\uE823", "Kisa sure bekleme eylemi.", "Runtime"),
        NewItem("AskUser", "\uE897", "Kullanicidan ek bilgi isteme eylemi.", "User"),
        NewItem("Stop", "\uE71A", "Agent dongusunu sonlandirma eylemi.", "Runtime")
    ];

    private string _searchText = string.Empty;

    public CapabilitiesViewModel()
    {
        Items = [];
        ApplyFilter();
    }

    public ObservableCollection<CapabilityViewItem> Items { get; }
    public int TotalCount => _allItems.Count;
    public int VisibleCount => Items.Count;
    public string CountSummary => $"{VisibleCount} / {TotalCount} planlanan eylem gosteriliyor";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public void ReloadFromRegistry()
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Items.Clear();
        var query = SearchText.Trim();
        var source = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item =>
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.TargetKindLabels.Any(label => label.Contains(query, StringComparison.OrdinalIgnoreCase)));

        foreach (var item in source)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(CountSummary));
    }

    private static CapabilityViewItem NewItem(string name, string icon, string description, string target) =>
        new()
        {
            Name = name,
            IconGlyph = icon,
            Description = description,
            TargetKindLabels = [target]
        };
}
