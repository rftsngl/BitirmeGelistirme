using System.Collections.ObjectModel;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class CapabilitiesViewModel : ObservableObject
{
    private readonly List<CapabilityViewItem> _allItems;
    private string _searchText = string.Empty;

    public CapabilitiesViewModel(IEnumerable<IActionHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var registered = handlers
            .Select(handler => handler.ActionName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _allItems = BuildCatalog()
            .Where(item => registered.Contains(item.Name) &&
                           DecisionSchema.SupportedActions.Contains(item.Name))
            .ToList();

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

    private static List<CapabilityViewItem> BuildCatalog() =>
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
        NewItem("jump_list", "\uE734", "Görev çubuğu Jump List günceller ve kısayol tıklamalarını açar.", "Windows API"),
        NewItem("com_invoke", "\uE8A5", "COM/OLE ProgID ile Office vb. otomasyon çağrısı.", "Windows API"),
        NewItem("verify_user", "\uE785", "Windows Hello ile kullanıcı doğrulaması ister.", "Windows API"),
        NewItem("global_hook", "\uE765", "Global klavye/fare hook başlatır/durdurur/okur.", "Windows API"),
        NewItem("service_control", "\uE7EF", "Windows servislerini listeler, durum okur, başlatır/durdurur.", "Windows API"),
        NewItem("event_log", "\uE7BA", "Application/System/Security olay günlüklerini okur.", "Windows API"),
        NewItem("registry_op", "\uE70D", "Kayıt defterinden okur/yazar/siler (HKCU/HKLM).", "Windows API"),
        NewItem("clipboard", "\uE77F", "Panodan okur, panoya yazar veya temizler.", "Windows API"),
        NewItem("install_package", "\uE896", "winget ile paket arar/kurar/kaldırır; Store listesi okur.", "Windows API"),
        NewItem("network_status", "\uE968", "Ağ bağlantısı, IP ve adaptör bilgisini döndürür.", "Windows API"),
        NewItem("audio_power", "\uE767", "Ses seviyesi, sessize alma ve uyku engelleme.", "Windows API"),
        NewItem("perf_counter", "\uE9D9", "CPU, bellek ve disk kullanım özeti.", "Windows API"),
        NewItem("file_search", "\uE721", "Windows Search ile dosya arar.", "Windows API"),
        NewItem("notification_listen", "\uEA8F", "Shell/Action Center olay günlüklerinden bildirim geçmişi okur.", "Windows API"),
        NewItem("shell_session", "\uE756", "Kalıcı PowerShell oturumu (start/write/read/stop).", "Windows API"),
        NewItem("file_watch", "\uE7B8", "Klasörde dosya değişikliklerini izler.", "Windows API"),
        NewItem("credential_store", "\uE785", "Windows Credential Manager listeler/okur/yazar/siler.", "Windows API"),
        NewItem("type_text", "\uE765", "Odaklı pencereye metin yazar (clipboard/SendInput).", "Klavye"),
        NewItem("press_key", "\uE92E", "Tek tuş gönderir.", "Klavye"),
        NewItem("press_shortcut", "\uE92E", "Klavye kısayolu gönderir (Ctrl+A...).", "Klavye"),
        NewItem("select_text", "\uE92E", "Metin seçimi: all, extend_*, word, line.", "Klavye"),
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
        NewItem("mouse_click", "\uE962", "Sol/sağ/orta tık; element veya koordinat (fallback).", "Mouse"),
        NewItem("mouse_move", "\uE962", "İmleci koordinata taşır (fallback).", "Mouse"),
        NewItem("mouse_scroll", "\uE962", "Mouse tekerleği ile kaydırır.", "Mouse"),
        NewItem("mouse_drag", "\uE962", "Mouse ile sürükle-bırak yapar (fallback).", "Mouse")
    ];

    private static CapabilityViewItem NewItem(string name, string icon, string description, string target) =>
        new()
        {
            Name = name,
            IconGlyph = icon,
            Description = description,
            TargetKindLabels = [target]
        };
}
