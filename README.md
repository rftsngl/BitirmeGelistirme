# Windows AI Assistant

Doğal dil komutlarını **tüm Windows masaüstünde** yürüten bir yapay zekâ asistanı. Kullanıcı yalnızca **hedefi** yazar (örn. "Not defterini aç ve merhaba yaz"); yöntemi, sırayı ve araç seçimini **LLM'in kendisi** belirler. Agent her özellik için ayrı bir senaryo/handler'a bağlı değildir: LLM az sayıda **genel yetenek ailesini** (kabuk/terminal, UI Automation, sistem/başlatma) adım adım birleştirir, her adımda zengin gözlem geri beslemesini (pencereler, UI ağacı, son aksiyon sonucu, kabuk çıktısı) okuyarak stratejisini uyarlar. Runtime'ın görevi bu yetenekleri LLM'e sunmak ve çıktıyı **strict JSON karar** olarak güvenli biçimde çalıştırmaktır. Güvenlik ağırlıklı olarak system prompt ve kaba `ActionGate` politikasıyla sağlanır.

## Mimari

Tek yönlü bağımlılık: `App → Agent → Runtime`

| Proje | Sorumluluk |
|-------|------------|
| `WindowsAiAssistant.App` | WinUI 3 arayüz (sohbet, sağlayıcı ayarları, **Ses ve Güvenlik**, yetenekler, geçmiş), tray, sesli overlay, DI kökü |
| `WindowsAiAssistant.Agent` | `AgentLoop`, `PromptBuilder`, `AiClient` (LLM), `DecisionParser`/`DecisionSchema` |
| `WindowsAiAssistant.Runtime` | Gözlem (ekran/pencere/UIA), aksiyon handler'ları, UI Automation, giriş enjeksiyonu, loglama |

Akış: **observe → decide (LLM, JSON) → execute → log** ve hedef tamamlanana ya da `MaxSteps`'e ulaşılana kadar tekrarlanır. Başarısız bir eylem akışı bitirmez; **geri bildirim olarak** bir sonraki adıma beslenir, böylece LLM alternatif bir yol dener (örn. `open_app` başarısızsa `shell` ile uygulamayı keşfedip `launch` eder). Yalnızca sağlayıcı/karar (altyapı) hataları akışı erken sonlandırır.

## Gereksinimler

- .NET 8 SDK
- Windows 10 sürüm 19041 (2004) veya üzeri
- Bir LLM erişimi: OpenAI uyumlu API, Google Gemini veya yerel sunucu (LM Studio / Ollama)

## Derleme ve çalıştırma

Derleme çıktıları varsayılan olarak **`%LOCALAPPDATA%\WindowsAiAssistantBuild`** altına yazılır (`Directory.Build.props`). Böylece proje klasöründeki `bin\Debug` DLL’leri antivirüs/sandbox tarafından kilitlenmez; Cursor/VS Code entegre terminalinden `dotnet build` güvenle çalışır.

```powershell
dotnet build WindowsAiAssistant.sln
dotnet run --project src/WindowsAiAssistant.App
```

### Antivirüs / MSB3021 (DLL erişim reddedildi)

Hata devam ederse (Controlled Folder Access veya agresif AV):

1. **Cursor/VS Code görevleri:** `Terminal` → `Run Task` → `build` (`.vscode/tasks.json`).
2. **Defender istisnaları (yönetici PowerShell):**
   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\scripts\Add-DevAntivirusExclusions.ps1
   ```
   Script proje klasörü, LocalAppData build çıktısı, `dotnet`, **Cursor.exe**, **Code.exe** ve MSBuild süreçlerini ekler.
3. **Temiz derleme:** görev `clean build output (LocalAppData)` veya:
   ```powershell
   Remove-Item -Recurse -Force "$env:LOCALAPPDATA\WindowsAiAssistantBuild" -ErrorAction SilentlyContinue
   dotnet build WindowsAiAssistant.sln
   ```

## Yapılandırma

Ayarlar şu sırayla çözülür:

1. **`src/WindowsAiAssistant.App/appsettings.json`** — varsayılanlar (`Agent`, `Runtime`, `Audio`).
2. **`appsettings.Local.json`** (opsiyonel, git'e dahil değil) — kullanıcı tercihleri (uyandırma kelimesi, hotkey vb.). Örnek: [`appsettings.Local.json.example`](src/WindowsAiAssistant.App/appsettings.Local.json.example).
3. **Sağlayıcı profilleri** — uygulama içi "Sağlayıcı Ayarları" sayfası; kalıcı dosya: `%LocalAppData%/WindowsAiAssistant/provider-settings.json`.
4. **API anahtarı** — önce profilde kayıtlı anahtar, yoksa profilin `ApiKeyEnvVar` ortam değişkeni (örn. `OPENAI_API_KEY`, `GEMINI_API_KEY`).

### Sağlayıcı türleri

| Tür | BaseUrl örneği | Not |
|-----|----------------|-----|
| OpenAI uyumlu | `https://api.openai.com/v1` | `Authorization: Bearer` |
| Gemini | `https://generativelanguage.googleapis.com` | `x-goog-api-key` başlığı |
| Yerel (LM Studio / Ollama) | `http://localhost:1234/v1` | API anahtarı gerekmez |

Vision (ekran görüntüsü) yalnızca profilde etkinse ve model multimodal ise kullanılır.

### Ses, tray ve overlay

- **Ayarlar** sayfası (`UnifiedSettingsPage`): hotkey, TTS, mikrofon izni, uyandırma kelimesi, ActionGate politikası, UI otomasyon seçenekleri.
- **Tray + arka plan:** `BackgroundModeEnabled` ile sistem tepsisinde çalışır; `Ctrl+Alt+A` (varsayılan) ile sesli overlay açılır.
- **Uyandırma kelimesi ve komut dinleme:** [Vosk](https://alphacephei.com/vosk/) ile tamamen yerel çalışır; Windows konuşma tanıma paketi veya API anahtarı gerekmez. Türkçe model (`vosk-model-small-tr-0.3`, ~35 MB) ilk kullanımda otomatik indirilir (`%LocalAppData%/WindowsAiAssistant/models`). Windows'ta Türkçe STT paketi yoksa komut dinleme otomatik olarak Vosk'a geçer.
- **Whisper STT:** `SpeechEngine=whisper` ve geçerli `WhisperModelPath` (ggml `.bin`) gerekir; yoksa Windows STT kullanılır. [Whisper.ggml modelleri](https://huggingface.co/ggerganov/whisper.cpp/tree/main) indirilebilir.
- **Yeniden başlatma:** Hotkey, wake-word, STT motoru ve UI otomasyon ayarları singleton servislerde tutulur; kayıttan sonra uygulama yeniden başlatılmadan etkinleşmez.

## Loglar

- **Run kayıtları:** `Runtime:LogsDirectory` (varsayılan `logs/runs/*.jsonl`) — her adım için gözlem, karar ve sonuç. Uygulamadaki "Geçmiş" sayfasından listelenir; sonuç kartındaki **Logu Aç** ile dosya konumu açılır.
- **Ekran görüntüleri:** `Runtime:ScreenshotsDirectory` (varsayılan `logs/screenshots`).

## Yönetici (elevated) pencereler

Standart kullanıcı hakkıyla çalışan bir uygulama, **yönetici olarak açılmış** pencereleri UI Automation ile kontrol edemez (Windows UIPI kısıtı). Yükseltilmiş uygulamaları hedeflemeniz gerekiyorsa Windows AI Assistant'ı **"Yönetici olarak çalıştır"** ile başlatın. DPI uyumu için uygulama Per-Monitor v2 manifesti ile gelir.

## Geliştirme planı

Fazlar ve durum: [`Docs/Plan/README.md`](Docs/Plan/README.md). Tamamlanan fazların iyileştirme backlog'u: [`Docs/Plan/tamamlanan-fazlar-iyilestirme-notlari.md`](Docs/Plan/tamamlanan-fazlar-iyilestirme-notlari.md).
