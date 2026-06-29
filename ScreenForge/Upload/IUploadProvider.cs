namespace ScreenForge.Upload;

public readonly record struct UploadResult(string Url, string ShareUrl);

/// <summary>
/// Görüntü yükleme sağlayıcısı soyutlaması. İlerideki backend'ler için genişletilebilir.
/// </summary>
public interface IUploadProvider
{
    /// <param name="imageBytes">Kodlanmış görüntü (PNG/JPEG/WebP).</param>
    /// <param name="mimeType">image/png, image/jpeg veya image/webp.</param>
    /// <param name="progress">0..1 arası ilerleme bildirimi (UI thread'e marshalling çağıran tarafın sorumluluğu).</param>
    Task<UploadResult> UploadAsync(byte[] imageBytes, string mimeType, Action<double>? progress = null);
}
