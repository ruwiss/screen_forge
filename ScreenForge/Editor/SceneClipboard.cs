using SkiaSharp;

namespace ScreenForge.Editor;

/// <summary>
/// Serbest mod kopyala/yapıştır/çoğalt için saf geometri ve sıralama yardımcıları.
/// UI'ya bağımlı değil — birim test edilebilir.
/// </summary>
public static class SceneClipboard
{
    public const float DuplicateOffset = 20f;

    /// <summary>Seçili öğeleri sahne z-sırasına göre (alttan üste) döndürür.</summary>
    public static List<SceneItem> OrderBySceneZ(Scene scene, IEnumerable<SceneItem> selected)
    {
        var set = selected as HashSet<SceneItem> ?? selected.ToHashSet();
        var ordered = new List<SceneItem>(set.Count);
        foreach (var item in scene.Items)
        {
            if (set.Contains(item))
                ordered.Add(item);
        }
        return ordered;
    }

    public static SKRect UnionBounds(IReadOnlyList<SceneItem> items)
    {
        if (items.Count == 0) return SKRect.Empty;
        var r = items[0].Bounds;
        for (int i = 1; i < items.Count; i++)
            r = SKRect.Union(r, items[i].Bounds);
        return r;
    }

    /// <summary>
    /// Dikdörtgen sol-üst köşesini sahne içine sıkıştırır.
    /// Genişlik/yükseklik sahneden büyükse sol/üst = 0.
    /// </summary>
    public static SKPoint ClampTopLeft(float left, float top, float width, float height, float sceneW, float sceneH)
    {
        if (width >= sceneW) left = 0;
        else left = Math.Clamp(left, 0, Math.Max(0, sceneW - width));
        if (height >= sceneH) top = 0;
        else top = Math.Clamp(top, 0, Math.Max(0, sceneH - height));
        return new SKPoint(left, top);
    }

    /// <summary>
    /// Union bounds'u anchor merkezli yerleştirir, sahne dışına taşmaz.
    /// Dönen (dx, dy) her öğeye uygulanacak ofset.
    /// </summary>
    public static (float Dx, float Dy) ComputeAnchorOffset(
        SKRect union, float anchorX, float anchorY, float sceneW, float sceneH)
    {
        float left = anchorX - union.Width / 2f;
        float top = anchorY - union.Height / 2f;
        var tl = ClampTopLeft(left, top, union.Width, union.Height, sceneW, sceneH);
        return (tl.X - union.Left, tl.Y - union.Top);
    }

    /// <summary>
    /// Çoğaltma: mevcut konumdan sabit ofset, sahne dışına taşmaz.
    /// </summary>
    public static (float Dx, float Dy) ComputeDuplicateOffset(
        SKRect union, float sceneW, float sceneH, float offset = DuplicateOffset)
    {
        float left = union.Left + offset;
        float top = union.Top + offset;
        var tl = ClampTopLeft(left, top, union.Width, union.Height, sceneW, sceneH);
        return (tl.X - union.Left, tl.Y - union.Top);
    }

    /// <summary>
    /// Seçili öğeleri saydam arka planla union bounds boyutunda bitmap'e çizer (sistem panosu için).
    /// </summary>
    public static SKBitmap RenderSelectionBitmap(IReadOnlyList<SceneItem> itemsInZOrder)
    {
        if (itemsInZOrder.Count == 0)
            throw new ArgumentException("En az bir öğe gerekli.", nameof(itemsInZOrder));

        var clones = itemsInZOrder.Select(i => i.Clone()).ToList();
        var union = UnionBounds(clones);
        float pad = 1f;
        float w = Math.Max(1, union.Width + pad * 2);
        float h = Math.Max(1, union.Height + pad * 2);
        float ox = -union.Left + pad;
        float oy = -union.Top + pad;

        var scene = new Scene
        {
            CanvasSize = new SKSize(w, h),
            BackgroundColor = SKColors.Transparent,
        };
        foreach (var c in clones)
        {
            c.Move(ox, oy);
            scene.Items.Add(c);
        }
        return SceneRenderer.RenderToBitmap(scene);
    }
}
