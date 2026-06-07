# Windows AI Assistant

Windows masaüstünüzde sesli veya yazılı komutlarla işlem yapan yapay zekâ asistanı. «Not defterini aç», «bu klasördeki dosyaları listele» veya «Chrome’da haberler sayfasını aç» gibi istekleri Türkçe söyleyebilir veya yazabilirsiniz; asistan adımları kendi planlayıp uygular.

---

## Ne yapar?

- **Sesli komut:** «Asistan» deyin veya kısayol tuşuna basın, konuşun.
- **Yazılı komut:** Uygulama içindeki sohbet ekranından yazın.
- **Masaüstü işlemleri:** Uygulama açma, dosya/klasör işlemleri, web sayfası, terminal komutları ve benzeri görevler.
- **Sesli yanıt:** İsterseniz cevapları Türkçe seslendirilir.
- **Onay isteyen işlemler:** Silme veya hassas adımlarda sizden onay alınır.

---

## Gereksinimler

- **Windows 10** (2004 / sürüm 19041) veya **Windows 11**
- **İnternet** (yapay zekâ sağlayıcısı ve sesli yanıt için; komut dinleme yerel çalışır)
- Bir **yapay zekâ hesabı** veya API anahtarı (OpenAI, Google Gemini veya yerel sunucu — LM Studio / Ollama)
- **Mikrofon** (sesli kullanım için)

---

## Kurulum

1. `WindowsAiAssistant-Setup` kurulum dosyasını çalıştırın.
2. Kurulum sihirbazını tamamlayın.
3. İlk açılışta **mikrofon izni** istenirse **İzin ver** deyin.

Kurulumdan sonra uygulama sistem tepsisinde (saat yanındaki simgeler) çalışabilir.

---

## İlk kurulum (5 dakika)

### 1. Yapay zekâ bağlantısı

Uygulamayı açın → **Sağlayıcı Ayarları**:

| Seçenek | Ne gerekir? |
|--------|-------------|
| OpenAI (ChatGPT API) | [platform.openai.com](https://platform.openai.com) üzerinden API anahtarı |
| Google Gemini | Google AI Studio API anahtarı |
| Yerel (LM Studio / Ollama) | Bilgisayarınızda çalışan yerel sunucu; anahtar gerekmez |

Anahtarı ilgili alana yapıştırıp **Bağlantıyı test et** ile deneyin. Başarılı olunca asistan kullanıma hazırdır.

### 2. Mikrofon

**Ayarlar → Ses** bölümünde:

- **Mikrofon izni ver** düğmesine basın.
- Doğru mikrofonu seçin (kulaklık/USB mikrofon kullanıyorsanız listeden onu seçin).

### 3. Sesli kullanım (isteğe bağlı)

- **Uyandırma kelimesi:** Açıkken «**Asistan**» diyerek başlatabilirsiniz.
- **Kısayol tuşu:** Varsayılan `Ctrl+Alt+A` — her yerden sesli paneli açar.

---

## Nasıl kullanılır?

### Sesli komut

1. **«Asistan»** deyin **veya** `Ctrl+Alt+A` tuşlarına basın.
2. Alttaki panelde **«Dinliyorum»** görünür — komutunuzu söyleyin.
3. Konuşmanız bitince **«Anlıyorum»** aşamasına geçer; ardından asistan işleme başlar.
4. Cevap sesli okunabilir; panelde yazılı özet de görünür.
5. Aynı oturumda **takip komutu** verebilirsiniz (ör. «şimdi kaydet»).

Panelde **X** ile iptal edebilirsiniz.

### Yazılı komut

Ana penceredeki **sohbet** alanına yazıp gönderin. Ses kullanmadan da tüm özellikler çalışır.

### Örnek komutlar

- «Hesap makinesini aç»
- «Masaüstündeki ekran görüntüsünü aç»
- «Chrome’da YouTube’u aç»
- «Bu klasördeki PDF dosyalarını listele»
- «Bugün hava nasıl?» (yalnızca sohbet)

Komutları doğal Türkçe ile verin; tam komut formatı ezberlemeniz gerekmez.

---

## Ayarlar (özet)

**Ayarlar** sayfasından yapılandırabilirsiniz:

| Bölüm | Açıklama |
|-------|----------|
| Ses | Mikrofon, uyandırma kelimesi, kısayol, sesli yanıt |
| Güvenlik | Hassas işlemlerde onay, sesle «evet/hayır» |
| Başlangıç | Windows açılışında arka planda başlat |
| Geliştirici modu | Teknik ayrıntıları gösterir (normal kullanımda kapalı bırakın) |

Ayarları kaydettikten sonra **ses ve kısayol değişiklikleri** için uygulamayı bir kez kapatıp açmanız gerekebilir.

---

## Sistem tepsisi

Uygulama arka planda çalışırken tepsi simgesine sağ tıklayın:

- **Ana pencereyi aç**
- **Sesli asistanı başlat** (overlay)
- **Dinlemeyi aç/kapat** (uyandırma + kısayol)
- **Çıkış**

Tepsi simgesi üzerine gelince «dinleniyor» veya «dinleme kapalı» yazar.

---

## Sık karşılaşılan durumlar

### «Asistan» dememe rağmen açılmıyor

- Tepside dinlemenin **açık** olduğundan emin olun.
- `Ctrl+Alt+A` ile deneyin — çalışıyorsa sorun uyandırma kelimesindedir.
- **Ayarlar → Ses:** Mikrofon izni ve doğru cihaz seçimi.
- Çok gürültülü ortamda **«Gerçek konuşma algılandığında uyan»** seçeneğini kapatıp tekrar deneyin.

### «Dinliyorum» ekranında uzun süre kalıyor

- Net ve yeterince yüksek sesle konuşun.
- Komuttan sonra kısa bir süre susun (sistem konuşmanızın bittiğini algılar).
- İlk komutta işlem birkaç saniye sürebilir (ses tanıma modeli yüklenir).

### «Ses duyamadım» uyarısı

- Windows **Ayarlar → Gizlilik → Mikrofon** bölümünde uygulama için izin verin.
- Başka bir uygulama mikrofonu kullanıyorsa kapatın (Discord, Zoom vb.).
- Farklı bir mikrofon seçip tekrar deneyin.

### Asistan bir işlemi yapamıyor

- **Yönetici olarak çalışan** programlar (bazı kurulum sihirbazları, Yönetici CMD) normal modda kontrol edilemez. Gerekirse uygulamayı **Yönetici olarak çalıştır** ile açın.
- İnternet bağlantısı ve API anahtarının geçerli olduğunu kontrol edin.
- **Geçmiş** sayfasından son çalışmanın hata mesajına bakın.

### Sesli yanıt gelmiyor

- **Ayarlar → Ses:** «Sesli yanıt» açık mı?
- Edge ses motoru internet gerektirir; kapalıysa Windows yerel sesi kullanılır.

---

## Gizlilik ve veri

- **Uyandırma kelimesi** ve **komut dinleme** büyük ölçüde bilgisayarınızda işlenir.
- **Yapay zekâ istekleri** seçtiğiniz sağlayıcıya (OpenAI, Gemini vb.) gider; komut metni ve gerekli bağlam paylaşılır.
- **Sesli yanıt (Edge)** Microsoft ses hizmetini kullanır; internet gerekir.
- İşlem kayıtları uygulama klasöründe tutulur; **Geçmiş** sayfasından görüntüleyebilirsiniz.

---

## Kaldırma

Windows **Ayarlar → Uygulamalar → Yüklü uygulamalar** listesinden **Windows AI Assistant** öğesini kaldırın.

---

## Destek

Sorun yaşarsanız:

1. Uygulamayı tamamen kapatıp yeniden açın.
2. Mikrofon izni ve API anahtarını kontrol edin.
3. Önce yazılı komutla (sohbet) deneyin — çalışıyorsa sorun yalnızca ses tarafındadır.

---

*Windows AI Assistant — masaüstünüz için sesli ve yazılı yapay zekâ yardımcısı.*
