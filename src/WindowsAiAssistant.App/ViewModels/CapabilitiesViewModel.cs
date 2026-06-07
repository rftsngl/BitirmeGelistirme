using System.Collections.ObjectModel;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class CapabilitiesViewModel : ObservableObject
{
    private readonly List<CapabilityViewItem> _allItems =
    [
        NewItem("respond", "\uE8BD", "Kullanıcıya nihai metin yanıtı döndürür.", "Sohbet"),
        NewItem("ask_user", "\uE897", "Kullanıcıdan ek bilgi ister.", "Sohbet"),
        NewItem("stop", "\uE71A", "Asistan döngüsünü sonlandırır.", "Runtime"),
        NewItem("wait", "\uE823", "Kısa süre bekler (seconds/ms).", "Runtime"),
        NewItem("open_app", "\uE7F4", "Uygulama adını Windows üzerinden açar; bilinen adlar alias olarak çözülür.", "Uygulama"),
        NewItem("open_url", "\uE774", "Varsayılan tarayıcıda URL açar.", "Tarayıcı"),
        NewItem("launch", "\uE7AC", "Herhangi bir exe/uri/komut başlatır (katalog dışı).", "Uygulama"),
        NewItem("shell", "\uE756", "PowerShell veya CMD ile komut çalıştırır ve stdout/stderr sonucunu sonraki adıma taşır.", "Kabuk"),
        NewItem("capture_screen", "\uE722", "Ekran görüntüsü alır (legacy veya Graphics Capture).", "Windows API"),
        NewItem("notify", "\uEA8F", "Toast / Action Center bildirimi gösterir.", "Windows API"),
        NewItem("wmi_query", "\uE946", "WMI/WQL ile sistem sorgusu çalıştırır.", "Windows API"),
        NewItem("schedule_task", "\uE787", "Task Scheduler ile görev oluşturur/siler/listeler.", "Windows API"),
        NewItem("jump_list", "\uE734", "Görev çubuğu Jump List günceller.", "Windows API"),
        NewItem("com_invoke", "\uE8A5", "COM/OLE ProgID ile Office vb. otomasyon çağrısı.", "Windows API"),
        NewItem("verify_user", "\uE785", "Windows Hello ile kullanıcı doğrulaması ister.", "Windows API"),
        NewItem("global_hook", "\uE765", "Global klavye/fare hook başlatır/durdurur/okur.", "Windows API"),
        NewItem("type_text", "\uE765", "Odaklı pencereye metin yazar (clipboard/SendInput).", "Klavye"),
        NewItem("press_key", "\uE92E", "Tek tuş gönderir.", "Klavye"),
        NewItem("press_shortcut", "\uE92E", "Klavye kısayolu gönderir (Ctrl+A...).", "Klavye"),
        NewItem("click_element", "\uE7C9", "UIA elementine tıklar (pattern -> mouse fallback).", "UI Element"),
        NewItem("focus_element", "\uE7B3", "UIA elementine odak verir.", "UI Element"),
        NewItem("read_element", "\uE890", "Element metnini/degerini okur.", "UI Element"),
        NewItem("set_value", "\uE70F", "Elemente değer yazar (ValuePattern).", "UI Element"),
        NewItem("select_element", "\uE762", "Liste/combo öğesini seçer.", "UI Element"),
        NewItem("expand_collapse", "\uE70D", "Menü/ağaç/combobox açar veya kapatır.", "UI Element"),
        NewItem("invoke_toggle", "\uE73A", "Checkbox/switch durumunu değiştirir.", "UI Element"),
        NewItem("scroll", "\uE8CB", "Element içinde kaydırır (up/down/left/right).", "UI Element"),
        NewItem("list_windows", "\uE8A5", "Görünür pencereleri listeler.", "Pencere"),
        NewItem("focus_window", "\uE737", "Pencereyi öne getirir/odaklar.", "Pencere"),
        NewItem("window_state", "\uE740", "Pencere durumu: minimize/maximize/restore/close.", "Pencere"),
        NewItem("move_window", "\uE759", "Pencereyi taşır/boyutlandırır.", "Pencere"),
        NewItem("mouse_click", "\uE962", "Koordinat veya element merkezine tıklar (fallback).", "Mouse"),
        NewItem("mouse_scroll", "\uE962", "Mouse tekerleği ile kaydırır.", "Mouse"),
        NewItem("mouse_drag", "\uE962", "Mouse ile sürükle-bırak yapar (fallback).", "Mouse")
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
    public string CountSummary => $"{VisibleCount} / {TotalCount} destekli eylem gösteriliyor";

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
