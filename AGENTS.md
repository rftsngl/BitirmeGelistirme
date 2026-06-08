# AGENTS.md — Windows AI Assistant

Bu dosya kod asistanları ve geliştiriciler için repo kurallarını özetler.

## Katmanlar

| Proje | Sorumluluk |
|-------|------------|
| `WindowsAiAssistant.App` | WinUI 3 arayüz, ses, tray, overlay, DI kökü |
| `WindowsAiAssistant.Agent` | LLM döngüsü, prompt, parse, task dispatch |
| `WindowsAiAssistant.Runtime` | Win32/UIA, action handler'lar, policy, observation |
| `WindowsAiAssistant.Tests` | Unit testler (xUnit) |

Bağımlılık yönü: **App → Agent → Runtime**. Runtime üst katmanlara bağımlı değildir.

## Build ve test

```bash
dotnet test
dotnet build src/WindowsAiAssistant.App/WindowsAiAssistant.App.csproj
```

- Hedef: `net8.0-windows10.0.19041.0`, Windows 10 2004+
- Sürüm: `Directory.Build.props` (App, Agent, Runtime ortak)
- Release: `.\scripts\release.ps1` (Test → Package → GitHub)
- App tam build ortamda Windows App SDK mimarisi gerektirebilir; test projesi her zaman çalıştırılmalıdır.

## Güvenlik ve politika

- Yeni fiziksel/UI action'lar `ActionGate.KnownActions` ve risk sınıflandırmasına eklenmelidir.
- Shell komutları `ShellSecurityPolicy` ile destructive olarak işaretlenir; onay akışı korunur.
- COM `com_invoke` allowlist dışına çıkmamalıdır (`Runtime.ComAutomation`).
- Vision kapalı profilde görüntü LLM'e gönderilmez (`VisionAttachmentPolicy`).
- Asistan kendi penceresine UI otomasyonu uygulanamaz (`AgentSelfWindow`).

## Observation

- UIA/screenshot hataları `UiCaptureSkipReason` ile prompt'a yansır; boş `catch` kullanmayın.
- Statik masaüstünde incremental reuse: `ObservationIncrementalPolicy` + fingerprint.
- Self-window odakta UIA atlanır.

## Yeni Runtime action ekleme checklist

1. `IActionHandler` implementasyonu (`Runtime/Actions/Handlers`)
2. `ActionGate.KnownActions` + `ClassifyRisk`
3. `AgentLoop.ContinueActions` (gerekirse)
4. `Program.cs` DI kaydı
5. Unit test (`ActionGateTests` veya handler testi)

## Test beklentisi

- Davranış değişikliği → ilgili unit test veya mevcut test güncellemesi
- Policy/güvenlik değişikliği → `ActionGateTests` veya allowlist testi
- Saf yardımcı fonksiyonlar → ayrı test sınıfı

## Plan ve denetim

- İyileştirme planı: [docs/plan/README.md](docs/plan/README.md)
- Denetim raporu: [docs/full-repo-audit.md](docs/full-repo-audit.md)
- Faz durumu: `docs/plan/L1/faz-*.md`

## Debug log

- `DebugAgentLog` varsayılan olarak Release'de kapalıdır (`Runtime.Logging.EnableDebugAgentLog`).
- DEBUG build'de varsayılan açıktır.

## Dil

- Kullanıcıya dönük metinler Türkçe.
- Kod yorumları ve log mesajları mevcut dosya stiline uygun (çoğunlukla Türkçe teknik).
