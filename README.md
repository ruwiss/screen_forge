# ScreenForge

Modern, hızlı **ekran alıntı ve düzenleme** aracı. Lightshot tarzı akış; çizim, adım, blur, serbest tuval, GIF ve buluta yükleme.

<p align="center">
  <img src="ScreenForge/Resources/app.png" alt="ScreenForge" width="128" />
</p>

<p align="center">
  <strong>.NET 9 · WPF · SkiaSharp</strong><br/>
  Türkçe arayüz · Windows 11 esinli koyu tema
</p>

---

## Özellikler

| Alan | Ne var? |
|------|---------|
| **Yakalama** | Bölge, tam ekran, serbest (kolaj) tuval |
| **Düzenleme** | Ok, şekil, kalem, highlight, metin, adım, blur/pixelate |
| **Serbest mod** | Çoklu seçim, kopyala / yapıştır / çoğalt, sistem panosu PNG |
| **Çıktı** | Kopyala, kaydet (PNG/JPEG/WebP), buluta yükle, GIF (bölge) |
| **Export** | Arkaplanlı / saydam + sağda kırp |
| **Sistem** | Tepsi ikonu, global kısayollar, ayarlar, otomatik güncelleme |

---

## Ekran görüntüleri

### Genel bakış

![ScreenForge genel bakış](docs/readme/hero.png)

Bölge seçimi ve mod çubuğu (Bölge · Tam ekran · Serbest).

### Açıklama araçları

![Açıklama araçları](docs/readme/annotations.png)

Ok, adım, highlight, metin, elips ve blur — seçimin üzerinde.

---

## Global kısayollar

| Eylem | Varsayılan |
|-------|------------|
| Bölge yakala | `Alt + Shift + S` |
| Tam ekran | `Win + Alt + F` |
| Tam ekran yükle | `Win + Alt + U` |
| Serbest / kolaj | `Win + Alt + C` |

Kısayollar **Ayarlar → Kısayollar** üzerinden değişir.

---

## Kurulum

### Sürüm paketi

`Releases/` altında:

- `ScreenForge-win-Setup.exe` — kurulum
- `ScreenForge-win-Portable.zip` — taşınabilir

### Kaynaktan

Gereksinim: [.NET 9 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet build ScreenForge.sln -c Release
dotnet run --project ScreenForge/ScreenForge.csproj -c Release
```

```bash
dotnet test ScreenForge.Tests/ScreenForge.Tests.csproj -c Release
```

README görselleri (opsiyonel):

```bash
dotnet run --project tools/ReadmeAssets -c Release -- docs/readme
```

---

## Lisans

Kişisel / dahili kullanım. Dağıtım için depo sahibine bakın.
