# Sistem Akisi - Katman Bazli Fikir Notlari

Bu belge, mevcut Windows AI Assistant karar yapisini daha anlasilir hale getirmek icin katmanlara ayirir. Her katmanda once mevcut davranis, sonra da gelistirme fikri aciklanir.

## Genel Mimari Fikri

Ana yonelim **AI-first + deterministic guardrails** modelidir.

Yani AI karar vermede daha aktif kullanilir; fakat sistemin guvenlik, dogrulama, onay, adim limiti ve fail-closed kurallari korunur. AI onerir, sistem sinir koyar.

Bu yaklasimin nedeni sudur: yuzlerce uygulama ve islem turu icin tek tek statik kural yazmak uzun vadede olceklenmez.

## Katman Ozeti

| Katman | Mevcut Gorev | Gelecek Fikri |
| --- | --- | --- |
| Komut girisi ve guvenlik | Bos/tehlikeli komutu elemek | AI safety-judge |
| Karar dongusu | LLM ile sonraki aksiyonu secmek | AI karar baglamini guclendirmek |
| Runtime slice | Verify/action/follow-up mikro akislarini calistirmak | AI step planner |
| Dogrulama | Aksiyon gercekten oldu mu kontrol etmek | AI aciklama katmani |
| Onay ve risk | Riskli adimlarda kullanici onayi istemek | AI destekli risk siniflandirma |

## Fikir 1 - SafetyGate'i AI Safety-Judge'a Cevirmek

### Mevcut Durum

`BasicSafetyGate`, komut metnini statik pattern listelerine gore kontrol eder. Tehlikeli bir ifade yakalarsa komutu bloklar. Policy eksikse fail-closed davranir.

### Fikir

Bos/tehlikeli komut ayrimini sadece pattern listesiyle yapmak yerine AI tabanli bir safety-judge kullanmak.

### Neden Gerekli?

Kullanicilar ayni tehlikeli niyeti farkli sekillerde ifade edebilir. Ayrica uygulama sayisi arttikca her varyasyon icin manuel pattern yazmak zorlasir.

### Beklenen Kazanim

AI, komutun kelimelerine degil niyetine ve baglamina bakarak daha esnek karar verebilir.

### Risk ve Onlem

AI yanlis karar verebilir. Bu nedenle AI belirsiz kalirsa, timeout olursa veya dusuk guven uretirse sistem otomatik fail-closed davranmalidir.

## Fikir 2 - Risk Siniflandirmasini AI ile Desteklemek

### Mevcut Durum

Risk seviyesi agirlikli olarak sabit policy ve action risk matrisiyle belirlenir. Ornegin `Focus` dusuk risk, `Launch` orta risk, `OpenFile` yuksek risk olarak ele alinir.

### Fikir

AI, komut + hedef + mevcut observation + aksiyon tipine bakarak `Allowed`, `RequiresApproval` veya `Denied` onerisi uretsin.

### Neden Gerekli?

Ayni aksiyon farkli baglamlarda farkli risk tasiyabilir. Ornegin dosya acmak genelde masum olabilir; ama sistem dosyasi, hassas konum veya belirsiz hedef riskli olabilir.

### Beklenen Kazanim

Daha duruma duyarlı risk puanlama yapilir ve kullanici onayi daha dogru yerde istenir.

### Risk ve Onlem

AI gereksiz onay isteyebilir veya riskli adimi kacirabilir. Bu nedenle son karar kullanici onayi, step-level policy ve fail-closed fallback ile dengelenmelidir.

## Fikir 3 - AI-First + Deterministic Guardrails Modeli

### Mevcut Durum

Sistemde hem LLM karar katmani hem de deterministic safety, verification ve approval kurallari vardir.

### Fikir

AI karar verme tarafinda daha aktif olsun; ama kritik sinirlar deterministic kalsin.

### Neden Gerekli?

Tamamen kural tabanli sistem olceklenmez. Tamamen AI tabanli sistem ise guvenlik acisindan kontrolsuz olabilir.

### Beklenen Kazanim

Esneklik ve guvenlik birlikte korunur. AI farkli senaryolari genelleyebilir, deterministic guardrail'ler sistemi sinirda tutar.

### Risk ve Onlem

Mimari biraz daha karmasik hale gelir. Bu nedenle her AI kararinda audit, aciklama, guven skoru ve fallback davranisi tutulmalidir.

## Fikir 4 - Runtime Slice Icine AI Step Planner Eklemek

### Mevcut Durum

Runtime slice akislari su an kodla tanimlanmis mikro workflow'lardir. Ornegin:

- process slice: verify process -> focus window
- file slice: verify file -> open file -> focus window
- service slice: verify status -> start/stop -> re-verify

### Fikir

Slice icinde follow-up adimini sabit kod yerine AI step planner onersin.

### Neden Gerekli?

Her yeni follow-up senaryosunu tek tek yazmak bakim maliyetini artirir. Uygulama ve islem sayisi arttikca deterministic slice sayisi sisebilir.

### Beklenen Kazanim

Slice daha esnek hale gelir. Sistem "bu adimdan sonra en mantikli guvenli adim ne?" sorusunu AI'a sorabilir.

### Risk ve Onlem

AI gereksiz veya riskli ekstra adim onerebilir. Bu nedenle zorunlu sinirlar:

- maksimum step-budget
- izinli action whitelist'i
- her adimda safety kontrolu
- her kritik adimdan sonra verification
- belirsizlikte fail-closed

## Fikir 5 - Verification Sonucuna AI Aciklama Katmani Eklemek

### Mevcut Durum

Verification sonucu deterministic olarak hesaplanir: `Verified`, `NotVerified`, `Inconclusive`, `Unsupported`.

### Fikir

Final verification status yine deterministic kalsin; ama AI bu sonucun neden olustugunu daha anlasilir dille aciklasin.

### Neden Gerekli?

`NotVerified` veya `Inconclusive` tek basina gelistirici ve kullanici icin bazen yeterince acik degildir.

### Beklenen Kazanim

Debug kolaylasir. Kullanici "neden olmadi?" sorusuna daha okunabilir cevap alir.

### Risk ve Onlem

AI aciklama uretirken karari etkilememelidir. AI burada sadece explain/summary rolunde kalmali; final status deterministic hesaplanmalidir.

## Fikir 6 - Statik Pattern Listelerinin Olceklenmeyecegini Kabul Etmek

### Mevcut Durum

Guvenlik ve riskin bir kismi pattern, allowlist ve sabit matrislerle yonetilir.

### Fikir

Pattern listeleri tamamen kaldirilmadan, ana karar stratejisi AI destekli hale getirilmeli.

### Neden Gerekli?

Yuzlerce uygulama, farkli pencere davranislari, farkli dosya turleri ve farkli kullanici niyetleri icin tek tek kural yazmak surdurulebilir degildir.

### Beklenen Kazanim

Sistem yeni uygulama ve komutlara daha hizli uyum saglar.

### Risk ve Onlem

AI'a fazla yuk bindirmek guvenlik riski dogurabilir. Bu nedenle pattern listeleri tamamen atilmaz; kritik deny/approval guardrail olarak korunabilir.

## Fikir 7 - AI Slice Olsa Bile Guardrail'leri Zorunlu Tutmak

### Mevcut Durum

Slice'lar belirli isleri guvenli hale getirmek icin verify/action/follow-up zinciri kurar.

### Fikir

AI slice icinde adim onerebilir; ancak her oneri calistirilmadan once sistem tarafindan sinirlanmalidir.

### Neden Gerekli?

AI'nin esnekligi yararli olsa da otomasyon katmaninda kontrolsuz ekstra adimlar tehlikeli olabilir.

### Beklenen Kazanim

AI destekli slice esnek olur, guardrail'ler sayesinde guvenli kalir.

### Risk ve Onlem

Riskli veya sonsuz adim zinciri olusabilir. Onlem olarak step-budget, approval gate, verification gate ve fail-closed davranisi zorunlu tutulur.

## Sonuc

Bu fikirler simdilik arastirma/backlog seviyesindedir. Aktif prototipte mevcut deterministic policy korunur. Nihai hedef, AI karar kalitesinden yararlanirken sistem guvenligini deterministic guardrail'lerle korumaktir.
