# Release Rehberi

Windows AI Assistant sürüm çıkarma, paketleme ve GitHub Release yayınlama adımları.

## Sürüm numarası

Semantic Versioning (`MAJOR.MINOR.PATCH`) kullanılır. Sürümü aşağıdaki dosyalarda **aynı değere** getirin:

| Dosya | Alan |
|-------|------|
| `Directory.Build.props` | `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` |
| `src/WindowsAiAssistant.App/app.manifest` | `assemblyIdentity@version` |
| `installer/WindowsAiAssistant.iss` | `MyAppVersion`, `VersionInfoVersion` |
| `CHANGELOG.md` | Yeni sürüm bölümü |
| `README.md` | Üst bilgi satırındaki sürüm |

## Ön koşullar

- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`global.json`: 8.0.412+)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (kurulum paketi için)

## 1. Doğrulama

```powershell
dotnet test
dotnet build src/WindowsAiAssistant.App/WindowsAiAssistant.App.csproj -c Release
```

## 2. Publish

```powershell
dotnet publish src/WindowsAiAssistant.App/WindowsAiAssistant.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:WindowsAppSDKSelfContained=true `
  -o dist/publish
```

> İlk build'de Whisper `medium` modeli indirilebilir (`scripts/ensure-whisper-medium.ps1`). Modeli kuruluma dahil etmek istemiyorsanız `-p:SkipWhisperModelOnPublish=true` ekleyin; kullanıcı Ayarlar'dan indirir.

## 3. Kurulum paketi (Inno Setup)

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/WindowsAiAssistant.iss
```

Çıktı: `dist/installer/WindowsAiAssistant-Setup-<sürüm>.exe`

## 4. GitHub Release

```powershell
git tag v1.1.0
git push origin v1.1.0

gh release create v1.1.0 `
  dist/installer/WindowsAiAssistant-Setup-1.1.0.exe `
  --title "Windows AI Assistant 1.1.0" `
  --notes-file .github/release-notes/v1.1.0.md
```

Release notları şablonu: [.github/release-notes/v1.1.0.md](.github/release-notes/v1.1.0.md)

## Kontrol listesi

- [ ] `dotnet test` yeşil
- [ ] Sürüm numaraları tüm dosyalarda eşleşiyor
- [ ] `CHANGELOG.md` güncellendi
- [ ] Kurulum paketi temiz Windows'ta test edildi
- [ ] Mikrofon izni ve sağlayıcı bağlantı testi çalışıyor
- [ ] Git tag ve GitHub Release oluşturuldu

## Dağıtım notları

- **Hedef platform:** x64, Windows 10 2004+ (19041)
- **Kurulum dizini:** `%ProgramFiles%\Windows AI Assistant`
- **Kaldırma:** Windows Ayarlar → Uygulamalar; `logs` klasörü kaldırma sırasında silinir
- **API anahtarları:** Credential Manager'da saklanır; kurulum paketine dahil edilmez
