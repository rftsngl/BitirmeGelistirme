namespace WindowsAiAssistant.App.Models;

/// <summary>
/// Ham action adlarini son kullanici icin anlasilir Turkce metinlere cevirir.
/// </summary>
public static class ActionDisplayHelper
{
    public static string ToUserFriendlyLabel(string? actionName) => (actionName ?? string.Empty) switch
    {
        "launch" or "open_app" => "Uygulama açıldı",
        "open_url" => "Bağlantı açıldı",
        "type_text" => "Metin yazıldı",
        "press_key" => "Tuş gönderildi",
        "press_shortcut" => "Kısayol kullanıldı",
        "select_text" => "Metin seçildi",
        "click_element" or "mouse_click" => "Bir öğeye tıklandı",
        "focus_element" => "Öğeye odaklanıldı",
        "read_element" => "Ekrandan bilgi okundu",
        "set_value" => "Değer ayarlandı",
        "select_element" => "Öğe seçildi",
        "expand_collapse" => "Menü/liste açıldı veya kapandı",
        "invoke_toggle" => "Anahtar değiştirildi",
        "scroll" or "mouse_scroll" => "Kaydırma yapıldı",
        "focus_window" => "Pencereye geçildi",
        "window_state" => "Pencere durumu değiştirildi",
        "move_window" => "Pencere taşındı",
        "list_windows" => "Pencereler listelendi",
        "mouse_move" => "İmleç taşındı",
        "mouse_drag" => "Sürükle-bırak yapıldı",
        "shell" => "Komut çalıştırıldı",
        "capture_screen" => "Ekran görüntüsü alındı",
        "notify" => "Bildirim gösterildi",
        "wmi_query" => "WMI sorgusu çalıştırıldı",
        "schedule_task" => "Zamanlanmış görev işlendi",
        "jump_list" => "Jump List güncellendi",
        "com_invoke" => "COM/OLE çağrısı yapıldı",
        "verify_user" => "Windows Hello doğrulaması",
        "global_hook" => "Global hook işlendi",
        "service_control" => "Windows servisi yönetildi",
        "event_log" => "Olay günlüğü okundu",
        "registry_op" => "Kayıt defteri işlendi",
        "clipboard" => "Pano işlendi",
        "install_package" => "Paket yöneticisi çalıştırıldı",
        "network_status" => "Ağ durumu okundu",
        "audio_power" => "Ses/güç ayarı uygulandı",
        "perf_counter" => "Performans ölçümü alındı",
        "file_search" => "Dosya araması yapıldı",
        "notification_listen" => "Bildirimler okundu",
        "shell_session" => "Shell oturumu işlendi",
        "file_watch" => "Dosya izleyici işlendi",
        "credential_store" => "Kimlik bilgisi kasası işlendi",
        "respond" => "Yanıt verildi",
        "ask_user" => "Bilgi istendi",
        "stop" => "İşlem durduruldu",
        "wait" => "Bekleniyor",
        _ => "İşlem yapıldı"
    };

    public static string ToRiskLabel(string? risk) => (risk ?? string.Empty).ToLowerInvariant() switch
    {
        "safe" or "normal" => "Normal",
        "sensitive" => "Hassas",
        "destructive" => "Yıkıcı",
        _ => risk ?? "Bilinmiyor"
    };
}
