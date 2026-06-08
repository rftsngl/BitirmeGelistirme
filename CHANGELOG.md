# Değişiklik Günlüğü

Bu proje [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) formatını ve [Semantic Versioning](https://semver.org/lang/tr/) sürüm numaralandırmasını izler.

## [1.1.0] — 2026-06-09

### Eklenen

- **Planlama ve doğrulama:** Görev öncesi plan oluşturma, adım ilerlemesi takibi ve tamamlama doğrulaması
- **Birleşik ayarlar sayfası:** Yapay zekâ modeli, sesli asistan, güvenlik ve geliştirici tercihleri tek ekranda
- **Sağlayıcı profilleri:** OpenAI, Google Gemini, Ollama ve LM Studio hazır şablonları; bağlantı testi
- **Vision desteği:** Profil bazında ekran görüntüsü gönderimi (açık/kapalı)
- **Sesli overlay oturumu:** Uyandırma kelimesi, global kısayol, takip komutu ve sesle onay
- **Whisper STT + Vosk uyandırma:** Yerel konuşma tanıma; ayarlardan model indirme
- **Geçmiş sayfası:** Çalışma kayıtları, filtreleme ve adım günlükleri
- **Yetenekler kataloğu:** 40+ destekli masaüstü eyleminin aranabilir listesi
- **Gelişmiş agent yetenekleri:** Skill router, task dispatch playbook'ları, Office/COM iş akışları
- **Ön plan odak yönetimi:** Masaüstü otomasyonu öncesi doğru pencereye odaklanma

### Değiştirilen

- README son kullanıcı odaklı olarak baştan yazıldı
- Ses ayarları ve transkripsiyon kalitesi iyileştirildi
- Gözlem (UIA + ekran) ve incremental reuse politikası güncellendi

### Güvenlik

- ActionGate risk sınıflandırması: shell yıkıcı kalıp listesi, COM allowlist, asistan penceresi koruması
- Hassas/yıkıcı eylemlerde UI ve sesli onay akışı

## [1.0.0] — 2025-12

### Eklenen

- WinUI 3 ana uygulama, sistem tepsisi ve arka plan modu
- Agent döngüsü (gözlem → LLM kararı → eylem)
- Temel masaüstü eylemleri: uygulama açma, URL, shell, UI otomasyonu
- OpenAI uyumlu ve Gemini sağlayıcı desteği
- Edge TTS ile Türkçe sesli yanıt
- Inno Setup kurulum paketi (x64)

[1.1.0]: https://github.com/rftsngl/BitirmeGelistirme/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/rftsngl/BitirmeGelistirme/releases/tag/v1.0.0
