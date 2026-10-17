# Ekran görüntüsü gecikmesi — 17 Ekim 2026

## Devam doğrulaması — 17 Ekim 2026

Erken ilk basış için önceki 2000 ms bekleme kaldırıldı. Ölçüm aracı artık
input-idle bildirimine ek olarak uygulamanın message-only kısayol penceresini
ve WM_NULL yanıtını doğrular. WPF pencere oluşturulması input-idle bildirimini
erken verebildiği için yalnız WaitForInputIdle yeterli değildir. İlk iki araştırma
koşusu (early-baseline.json ve early-ready-after.json) bu yüzden performans
karşılaştırması olarak kullanılmaz; başarısız kayıtları korunmuştur.

Ürün değişikliği: App.WarmupCapturePathAsync, arka plan BitBlt ısınmasını başlatır
ve tamamlanmasını beklemeden UI üzerinde PrepareOverlay çağırır. Hazırlık artık
Background dispatcher callback'i yerine kısayol kaydından önce başlar.
Yakalama yolu ve pencere sözleşmeleri korunmuştur.

| Ölçüm | Eski başlangıç | Yeni başlangıç | Yeni tekrar | Yeni, +2000 ms bekleme |
|---|---:|---:|---:|---:|
| Erken ilk basış → güncel donmuş piksel | 514,5 ms | 269,0 ms | 283,4 ms | 217,9 ms |
| Sonraki 19 basış medyanı | 189,9 ms | 177,2 ms | 179,2 ms | 181,2 ms |
| ≤200 ms | 13/20 | 16/20 | 15/20 | 18/20 |
| Süreç başlangıcı → kısayol hazır | 658,3 ms | 845,5 ms | 867,3 ms | 814,4 ms |
| Süreç başlangıcı → ilk tuş | 680,7 ms | 876,4 ms | 890,9 ms | 2840,0 ms |
| Süreç başlangıcı → ilk donmuş piksel | 1195,2 ms | 1145,3 ms | 1174,3 ms | 3058,0 ms |
| Tazelik / güncel ilk overlay karesi | 20/20 / 20/20 | 20/20 / 20/20 | 20/20 / 20/20 | 20/20 / 20/20 |
| Kaydet / kopyala | geçti / geçti | geçti / geçti | geçti / geçti | geçti / geçti |

**150–200 ms hedefi ilk basışta henüz sağlanmadı.** Yeni akış hazırlığın bir
kısmını kısayol kaydından önceye taşır; süreç başlangıcından ilk görüntüye toplam
kazanım 21–50 ms civarındadır. Tuştan görüntüye kazancın tamamını toplam açılış
hızlanması diye yorumlamak doğru olmaz. İki erken süreç ve bir ısınmış süreç
ölçüldü; bu örneklem tüm yükler için garanti vermez.

Ham kayıtlar: early-hotkey-before.json, early-hotkey-after.json,
early-hotkey-after-repeat.json, warm-after-continuation.json.
Her kayıt DLL SHA-256, gerçek başlangıç süreleri, 20 piksel ve 20 basış içerir.

Başlangıç paketi, değiştirilmiş paket ve geri alınmış ayrı proje kopyası:
her birinde **389 geçti, 0 başarısız, 0 atlanan**. Geri alma betiği ayrı kopyada
çalıştırıldı; App.xaml.cs ve CaptureProbe/Program.cs özgün baytlara döndü.
Asıl kaynakta değişiklik bırakıldı. Kayıt/GIF/sunum/çeviri/altyazı ve csproj
için 69 dosyanın devam başlangıcındaki kopyayla SHA-256 eşitliği doğrulandı.

Komutlar, gerçek çıktılar, hash'ler ve geri alma:
[VERIFICATION.txt](continuation-2026-10-17/VERIFICATION.txt).

Aşağıdaki rapor önceki oturumun ölçümleridir; yukarıdaki devam sonuçları bunlardan ayrıdır.

## Önceki oturumun sonucu

Ekran görüntüsü yalnız `GetDesktopWindow` → `GetWindowDC` → `BitBlt` kullanır.
DXGI/kare önbelleği ekran görüntüsü yolundan kaldırıldı. Video kaydının
`Record/DxgiFrameSource.cs` dosyasına ve GIF, sunum, çeviri, altyazı kaynaklarına dokunulmadı.

Son ölçümde ilk basış **186,8 ms**, sonraki basışların medyanı **160,2 ms**.
**200 ms katı tavanı sağlanmış değildir:** 20 basışın 18'i bu sınırda/altında;
iki aşım **203,3 ms** ve **223,3 ms**. Tazelik testinde ve ilk görülen overlay
pikselinde 20/20 doğrulandı. Bu, bütün makineler ve yükler için yüzde yüz gecikme garantisi değildir.

## Değişiklikler

- `ScreenForge/Capture/ScreenCapture.cs`: SAF BitBlt; başarısız yakalama hata verir,
  kısmi/geçersiz bitmap döndürülmez. Başlangıç ısınmasının görüntüsü saklanmaz.
- `ScreenForge/Capture/DxgiScreenCache.cs`: ekran görüntüsüne özgü önbellek kaldırıldı.
- `ScreenForge/App.xaml.cs`: eski ekran görüntüsüyle değil opak, boş yüzeyle hazırlık;
  gereksiz başlangıç beklemesi kaldırıldı. Monitör düzeni değişince hazır pencere yenilenir.
  Başarısız kurulumda pencere/bitmap sahipliği temizlenir.
- `ScreenForge/Windows/CaptureOverlayWindow.xaml.cs`: DWM cloak ile görünmez gerçek
  pencere/render ısınması; desteklenmezse ekran dışı hazırlık. Yeni yakalama yüklenince
  önceki hazırlık callback'leri etkisizleşir. Render/DWM sonrasına bırakılan odak,
  gereksiz ikinci `BringWindowToTop` çağrısı olmadan alınır.
- `ScreenForge/Settings/AppSettings.cs`: süreç + GUID içeren ayrı geçici dosyadan
  atomik rename; hata halinde yalnız o geçici dosya temizlenir. Okuyucu rename'i
  engellemez. Bozuk dosya silinmez; `.corrupt` yedeği saklanır (`null` JSON dahil).
- Türkçe yorumlar, mevcut sınıflar/sözleşmeler, klasör düzeni ve XAML korundu.
  `WM_HOTKEY` için `DispatcherPriority.Send` ve `MoveWindow(..., false)` yerel
  kaynakta zaten vardı; korundu. Sürüm alanı başlangıçta `1.2.9` idi, değiştirilmedi.

## Karşılaştırma

Sanal ekran: **4480 × 2166 fiziksel piksel**, üç monitör, PerMonitorV2; .NET **9.0.8**.
Her süreçte ilk basıştan önce 2000 ms başlangıç ısınması beklenmiştir; bu süre
tuştan donmuş piksele gecikmeye dahil değildir. Isınma tamamlanmadan yapılan
basışın gecikmesine ilişkin bir garanti çıkarılamaz; hazırlıkla yarışma güvenliği ayrı testtedir.

| Ölçüm | Önce: yalnız BitBlt'e çevrilmiş eski pencere yolu | Sonra |
|---|---:|---:|
| İlk basış | 295,5 ms | 186,8 ms |
| Sonraki 19 basışın medyanı | 173,8 ms | 160,2 ms |
| Sonraki basışların aralığı | 166,0–255,6 ms | 138,8–223,3 ms |
| ≤200 ms basış | 14/20 | 18/20 |
| Canlı yakalama tazeliği | 20/20 | 20/20 |
| İlk görülen overlay pikseli güncel | 20/20 | 20/20 |
| Bölge → pano / yeni PNG | geçti / geçti | geçti / geçti |

Önce kontrolü, görev başındaki kaynakların geçici kopyasında yalnız yakalama ve
ısınmayı BitBlt'e zorlar; eski overlay/odak/başlangıç davranışını değiştirmez.
Böylece pencere iyileştirmesi, hızlı ama yanlış kare verebilen DXGI ile karşılaştırılmaz.
Ham veriler: `before.json`, `after.json`; kullanılan DLL'lerin SHA-256 değerleri bu dosyalardadır.

Görev başındaki **değiştirilmemiş DXGI/önbellekli DLL**, aynı son renk uyarıcısıyla
ayrıca sınandı: **8/20** taze kare, beklenen çıkış kodu **1**.
Kanıt: `before-dxgi-freshness.json`. Daha düşük DXGI kopyalama süresi geçerli bir
tazelik çözümü olarak kabul edilmemiştir.

## Testler ve uçtan uca doğrulama

```powershell
dotnet test ScreenForge.sln -c Release --no-restore --logger "trx;LogFileName=final-solution.trx"
```

**389 geçti, 0 başarısız, 0 atlanan.** Görev başında 385 test vardı: kaldırılan
DXGI önbelleğine ait dört test emekliye ayrıldı; sekiz davranış testi eklendi.
Kalan mevcut testler ve `DispatcherBeginInvokeTests` yeşil.

- Hazırlık sürerken kısayol: gecikmiş callback aktif pencereyi gizleyemez.
- Yeni bitmap hem WPF gösterimi hem dışa aktarım/kırpma kaynağı olur; eski bitmap bırakılır.
- Hazırlık/kapanış callback'leri; görünmez ısınmanın masaüstü pikselini/odağı değiştirmemesi.
- Eşzamanlı ayar yazma/okuma, başarısız rename temizliği, bozuk JSON'un korunması.
- Canlı renk değişimi → hemen BitBlt → piksel: 20/20. Beş ayrı test sürecinde ayrıca **100/100**.
- Gerçek EXE, gerçek `Ctrl+Shift+S`, 20 ayrı oturum; ilk ve art arda basışlar.
- Gerçek fare sürüklemesiyle bölge seçimi, `Ctrl+C`, `Ctrl+S`, dosya iletişim kutusu.
  Panodaki ve yeni PNG'deki merkez pikseli güncel renkle eşleşti. PNG yolları JSON'dadır.

Bir ara tam test koşusunda uyarıcı 19/20 verdi. GDI çizim grubunun DWM bekleyişinden
önce gönderilmesi için test uyarıcısına `GdiFlush` eklendi; yakalamaya gecikme,
yeniden deneme veya önbellek eklenmedi. Sonrasında beş ayrı tazelik koşusu ve tam
test paketi geçti. Eski DXGI kontrolünün hâlâ 8/20 vermesi de kaydedildi.

`CaptureProbe`, `Show()`/render olayı zamanını değil, gerçek masaüstü pikselini ölçer.
Ölçüm öncesi giriş aracının JIT/interop'u hazırlanır. Tazelik deneyinde renk çizimi
ile yakalama arasında uyku yoktur: `UpdateWindow` → `GdiFlush` → `DwmFlush` → BitBlt.
Yalnız gecikme deneyinde saat başlamadan ham renk ekranda ayrıca doğrulanır.
Yazılımsal piksel gözlemi GDI/DWM/zamanlayıcı maliyeti içerir; foton gecikmesi değildir.

Araç ve yeniden çalıştırma açıklaması: `tools/CaptureProbe/README.md`.
Geçici ayar değişiklikleri ve başlangıç kaydı araç tarafından geri alınır.
Kaynak hash karşılaştırmasında `Record`, `Gif`, `Presenter`, `Translate`, `Subtitle`
ve `ScreenForge.csproj` değişmedi. PR açılmadı.
