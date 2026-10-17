# CaptureProbe

Windows masaüstünde çalışan, tekrarlanabilir tazelik/gecikme ve dışa aktarım aracı.
Ürün kodunda test kancası veya farklı yakalama yolu yoktur.

## Çalıştırma

Çalışan ScreenForge'u kapatın ve test sürerken masaüstünü kullanmayın. Proje kökünde:

```powershell
dotnet run --project tools/CaptureProbe -c Release -- "ScreenForge/bin/Release/net9.0-windows10.0.19041.0/ScreenForge.exe" "docs/performance/after.json"
```

Yalnız yirmi canlı BitBlt yakalaması:

```powershell
dotnet run --project tools/CaptureProbe -c Release -- --freshness "docs/performance/freshness.json"
```

İsteğe bağlı üçüncü argüman, uygulamanın giriş kuyruğu ve kendi kısayol penceresi
WM_NULL yanıtıyla hazır olduktan sonra beklenen ek milisaniyedir (varsayılan `2000`).
Erken ilk basış için `0` kullanın. `inputIdleMs`, `hotkeyReadyMs` ve
`firstInputSinceStartMs` süreç başlangıcından geçen gerçek süreleri raporlar.
Bu hazırlık ve ek bekleme tuştan donmuş piksele gecikmeye dahil değildir.

## Ölçülenler

- PerMonitorV2 fiziksel koordinatları; GDI ile çizilen bağımsız renk penceresi.
- Her renk `UpdateWindow` + `GdiFlush` + `DwmFlush` ile gerçekten sunulduktan **hemen** sonra yakalama.
  Renk değişimiyle yakalama arasında `Sleep` veya kare bekleme yoktur.
- Yirmi yakalamada beklenen ve gelen piksel, her yakalamanın süresi.
- Verilen EXE'nin yanındaki `ScreenForge.dll` ayrı yükleme bağlamında kullanılır.
  Böylece eski EXE ölçülürken yanlışlıkla yeni yakalayıcı test edilmez. DLL SHA-256 rapora yazılır.
- Gerçek uygulama süreci ve gerçek `WM_HOTKEY` yolu: `SendInput` ile `Ctrl+Shift+S`.
  Aracın giriş dizisi/JIT hazırlığı ölçümden önce yapılır.
- Gecikme sayacı başlamadan uyarıcının ham rengi masaüstü pikselinde doğrulanır.
  Bu bekleme yalnız gecikme deneyindedir; yukarıdaki yirmi tazelik denemesinde yoktur.
- Giriş gönderiminin hemen öncesinden, masaüstündeki pikselin güncel görüntünün
  karartılmış rengine dönüşmesine kadar `Stopwatch` ölçümü. İlk piksel değişimi de ayrıca kaydedilir.
  Pencere `Show()` süresi veya `ContentRendered` zamanı görünür kare yerine kullanılmaz.
- İlk basış ve sonraki on dokuz basış; her oturumda değişen renk ve ilk görülen pikselin tazeliği.
- Son iki basışta gerçek fare sürüklemesiyle bölge seçimi, `Ctrl+C` ve `Ctrl+S`.
  Panodaki görüntünün ve **yeni oluşturulmuş**, benzersiz adlı PNG'nin merkez pikseli doğrulanır.

Araç kişisel ayarları geçici olarak değiştirir ve `finally` içinde özgün baytları geri yazar;
Windows başlangıç kaydı da korunur. Pano, kopyalama testinin görüntüsünü içerir.
Araç yalnız kendi başlattığı uygulama sürecini kapatır. Geçersiz piksel/dışa aktarım veya
diğer hata durumunda çıkış kodu `1`, başarılı doğrulamada `0` olur.

`GetPixel` gözlemi DWM/GDI eşzamanlaması ve Windows zamanlayıcısından etkilenir; sonuç
fiziksel tuş anahtarının gecikmesi veya monitörün foton gecikmesi değildir. Önce/sonra
aynı araç, sanal ekran ve giriş yöntemiyle karşılaştırılmalıdır.

Eski bir yakalama DLL'sini aynı renk uyarıcısıyla sınamak için `--freshness` kipinde
üçüncü argüman olarak o DLL'nin tam yolunu verebilirsiniz. Başarısız eski sonuç
bekleniyorsa çıkış kodunun `1` olması da beklenir; araç bu başarısızlığı gizlemez.
