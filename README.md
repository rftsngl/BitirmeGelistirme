# Windows AI Assistant

Doğal dil komutlarını **tüm Windows masaüstünde** UI Automation ile yürüten bir yapay zekâ asistanı. Kullanıcı bir hedef yazar (örn. "Not defterini aç ve merhaba yaz"); agent gözlem yapar, bir LLM'den **strict JSON karar** alır, kararı `ActionExecutor` üzerinden çalıştırır ve adım adım ilerler.

## Mimari

Tek yönlü bağımlılık: `App → Agent → Runtime`

| Proje | Sorumluluk |
|-------|------------|
| `WindowsAiAssistant.App` | WinUI 3 arayüz (sohbet, sağlayıcı ayarları, yetenekler, geçmiş), DI kökü |
| `WindowsAiAssistant.Agent` | `AgentLoop`, `PromptBuilder`, `AiClient` (LLM), `DecisionParser`/`DecisionSchema` |
| `WindowsAiAssistant.Runtime` | Gözlem (ekran/pencere/UIA), aksiyon handler'ları, UI Automation, giriş enjeksiyonu, loglama |

Akış: **observe → decide (LLM, JSON) → execute → log** ve hedef tamamlanana ya da `MaxSteps`'e ulaşılana kadar tekrarlanır.

## Gereksinimler

- .NET 8 SDK
- Windows 10 sürüm 19041 (2004) veya üzeri
- Bir LLM erişimi: OpenAI uyumlu API, Google Gemini veya yerel sunucu (LM Studio / Ollama)

## Derleme ve çalıştırma

```powershell
dotnet build WindowsAiAssistant.sln
dotnet run --project src/WindowsAiAssistant.App
```

## Yapılandırma

Ayarlar şu sırayla çözülür:

1. **`src/WindowsAiAssistant.App/appsettings.json`** — varsayılanlar (`Agent`, `Runtime`).
2. **`appsettings.Local.json`** (opsiyonel, git'e dahil değil) — yerel override. Örnek: [`appsettings.Local.json.example`](src/WindowsAiAssistant.App/appsettings.Local.json.example).
3. **Sağlayıcı profilleri** — uygulama içi "Sağlayıcı Ayarları" sayfası; kalıcı dosya: `%LocalAppData%/WindowsAiAssistant/provider-settings.json`.
4. **API anahtarı** — önce profilde kayıtlı anahtar, yoksa profilin `ApiKeyEnvVar` ortam değişkeni (örn. `OPENAI_API_KEY`, `GEMINI_API_KEY`).

### Sağlayıcı türleri

| Tür | BaseUrl örneği | Not |
|-----|----------------|-----|
| OpenAI uyumlu | `https://api.openai.com/v1` | `Authorization: Bearer` |
| Gemini | `https://generativelanguage.googleapis.com` | `x-goog-api-key` başlığı |
| Yerel (LM Studio / Ollama) | `http://localhost:1234/v1` | API anahtarı gerekmez |

Vision (ekran görüntüsü) yalnızca profilde etkinse ve model multimodal ise kullanılır.

## Loglar

- **Run kayıtları:** `Runtime:LogsDirectory` (varsayılan `logs/runs/*.jsonl`) — her adım için gözlem, karar ve sonuç. Uygulamadaki "Geçmiş" sayfasından listelenir; sonuç kartındaki **Logu Aç** ile dosya konumu açılır.
- **Ekran görüntüleri:** `Runtime:ScreenshotsDirectory` (varsayılan `logs/screenshots`).

## Yönetici (elevated) pencereler

Standart kullanıcı hakkıyla çalışan bir uygulama, **yönetici olarak açılmış** pencereleri UI Automation ile kontrol edemez (Windows UIPI kısıtı). Yükseltilmiş uygulamaları hedeflemeniz gerekiyorsa Windows AI Assistant'ı **"Yönetici olarak çalıştır"** ile başlatın. DPI uyumu için uygulama Per-Monitor v2 manifesti ile gelir.

## Geliştirme planı

Fazlar ve durum: [`Docs/Plan/README.md`](Docs/Plan/README.md). Tamamlanan fazların iyileştirme backlog'u: [`Docs/Plan/tamamlanan-fazlar-iyilestirme-notlari.md`](Docs/Plan/tamamlanan-fazlar-iyilestirme-notlari.md).
