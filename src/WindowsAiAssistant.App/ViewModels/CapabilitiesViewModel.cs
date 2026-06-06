using System.Collections.ObjectModel;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class CapabilitiesViewModel : ObservableObject
{
    private readonly List<CapabilityViewItem> _allItems =
    [
        NewItem("respond", "\uE8BD", "Kullaniciya nihai metin yaniti dondurur.", "Sohbet"),
        NewItem("ask_user", "\uE897", "Kullanicidan ek bilgi ister.", "Sohbet"),
        NewItem("stop", "\uE71A", "Agent dongusunu sonlandirir.", "Runtime"),
        NewItem("wait", "\uE823", "Kisa sure bekler (seconds/ms).", "Runtime"),
        NewItem("open_app", "\uE7F4", "Katalogdaki uygulamayi acar (notepad, calc, cmd...).", "Uygulama"),
        NewItem("open_url", "\uE774", "Varsayilan tarayicida URL acar.", "Tarayici"),
        NewItem("launch", "\uE7AC", "Herhangi bir exe/uri/komut baslatir (katalog disi).", "Uygulama"),
        NewItem("type_text", "\uE765", "Odakli pencereye metin yazar (clipboard/SendInput).", "Klavye"),
        NewItem("press_key", "\uE92E", "Tek tus gonderir.", "Klavye"),
        NewItem("press_shortcut", "\uE92E", "Klavye kisayolu gonderir (Ctrl+A...).", "Klavye"),
        NewItem("click_element", "\uE7C9", "UIA elementine tiklar (pattern -> mouse fallback).", "UI Element"),
        NewItem("focus_element", "\uE7B3", "UIA elementine odak verir.", "UI Element"),
        NewItem("read_element", "\uE890", "Element metnini/degerini okur.", "UI Element"),
        NewItem("set_value", "\uE70F", "Elemente deger yazar (ValuePattern).", "UI Element"),
        NewItem("select_element", "\uE762", "Liste/combo ogesini secer.", "UI Element"),
        NewItem("expand_collapse", "\uE70D", "Menu/agac/combobox acar veya kapatir.", "UI Element"),
        NewItem("invoke_toggle", "\uE73A", "Checkbox/switch durumunu degistirir.", "UI Element"),
        NewItem("scroll", "\uE8CB", "Element icinde kaydirir (up/down/left/right).", "UI Element"),
        NewItem("list_windows", "\uE8A5", "Gorunur pencereleri listeler.", "Pencere"),
        NewItem("focus_window", "\uE737", "Pencereyi one getirir/odaklar.", "Pencere"),
        NewItem("window_state", "\uE740", "Pencere durumu: minimize/maximize/restore/close.", "Pencere"),
        NewItem("move_window", "\uE759", "Pencereyi tasir/boyutlandirir.", "Pencere"),
        NewItem("mouse_click", "\uE962", "Koordinat veya element merkezine tiklar (fallback).", "Mouse"),
        NewItem("mouse_scroll", "\uE962", "Mouse tekerlegi ile kaydirir.", "Mouse"),
        NewItem("mouse_drag", "\uE962", "Mouse ile suruekle-birak yapar (fallback).", "Mouse")
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
    public string CountSummary => $"{VisibleCount} / {TotalCount} destekli eylem gosteriliyor";

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
