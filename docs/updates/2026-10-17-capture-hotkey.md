# 2026-10-17 — SAF BitBlt yakalama, hızlı overlay ve atomik ayarlar

- Ekran görüntüsü GetDesktopWindow → GetWindowDC → BitBlt yolunu kullanır; ekran görüntüsü için DXGI/kare önbelleği eklenmez. Eski PR #1'in yerini alır.
- Overlay boş, opak yüzeyle DWM cloak altında hazırlanır; önceki ekran görüntüsü saklanmaz.
- HWND/XAML/render hazırlığı kısayol kaydından önce başlar ve arka plan BitBlt ısınmasıyla paralel yürür.
- Hazırlık callback'leri aktif yakalamayı sonradan gizleyemez; ekran düzeni değişince hazır pencere yenilenir. Bitmap sahipliği ve hata temizliği korunur.
- Görünmez pencerenin MoveWindow repaint'i kaldırılır; odak ve pencere listesi ilk render sonrasına bırakılır. WM_HOTKEY DispatcherPriority.Send kullanır.
- Ayarlar süreç/GUID içeren geçici dosyadan atomik taşınır; bozuk JSON .corrupt olarak korunur; eşzamanlı okuma/yazma testleri eklendi.
- CaptureProbe gerçek Ctrl+Shift+S, değişen masaüstü pikseli, güncel ilk overlay karesi, bölge seçimi, PNG kaydı ve pano kopyasını doğrular. Giriş kuyruğu yanında kısayol HWND/WM_NULL hazırlığı ve başlangıç süreleri ölçülür.
- Son üç uygulama koşusunda 60/60 taze yakalama, 60/60 güncel ilk overlay karesi; kaydet/kopyala her koşuda geçti.
- Erken ilk basış: eski başlangıç 514,5466 ms; yeni başlangıç 268,9526 ve 283,4211 ms. Tekrarlı basış medyanı 189,9227 → 177,2063–179,1705 ms.
- Hazırlığın bir kısmı başlangıca taşınır: süreç başlangıcından ilk donmuş piksele 1195,2197 → 1145,3334–1174,3442 ms.
- Isınmış koşu: ilk basış 217,9351 ms; sonraki medyan 181,1573 ms; 18/20 basış ≤200 ms. **150–200 ms hedefi her basışta sağlanmış değildir.**
- PR kaynak ağacında dotnet test ScreenForge.sln -c Release: 389 geçti, 0 başarısız, 0 atlanan; çıkış 0. Ayrı kopyada geri alma betiği ve 389/389 test doğrulandı.
- Video/GIF, sunum, çeviri ve altyazı kaynakları korunur. PR sürümü main ile aynı 1.2.8 bırakıldı; yerel test raporlarındaki 1.2.9 görev öncesi yerel farktı.
- Ayrıntılı ölçümler ve ham kayıtlar: docs/performance/capture-latency.md; araç: tools/CaptureProbe/README.md; geri alma ve komut çıktıları: docs/performance/continuation-2026-10-17/VERIFICATION.txt.

Tarih: 2026-10-17 19:41 Europe/Istanbul
