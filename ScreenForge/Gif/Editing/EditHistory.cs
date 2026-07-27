namespace ScreenForge.Gif.Editing;

/// <summary>Geri alınabilir işlemin hangi sisteme ait olduğu.</summary>
public enum EditScope
{
    /// <summary>Kare düzenlemesi (sil, çoğalt, kırp, döndür).</summary>
    Frames,

    /// <summary>Çizim düzenlemesi (nesne ekle, taşı, sil).</summary>
    Annotation,
}

/// <summary>
/// Kare ve çizim düzenlemelerinin ortak sırası.
/// </summary>
/// <remarks>
/// Belge ve sahne kendi geri alma yığınlarını tutar; bu sınıf yalnızca
/// <b>hangisinin sırada olduğunu</b> bilir. Böylece kullanıcı Ctrl+Z'ye
/// bastığında en son yaptığı iş geri alınır — kare mi çizim mi olduğuna
/// bakılmaksızın.
/// </remarks>
public sealed class EditHistory
{
    private readonly List<EditScope> _undo = new();
    private readonly List<EditScope> _redo = new();
    private readonly int _limit;

    public EditHistory(int limit = 100) => _limit = Math.Max(1, limit);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Sırada geri alınacak işlemin türü; yoksa <see langword="null"/>.</summary>
    public EditScope? NextUndo => _undo.Count > 0 ? _undo[^1] : null;

    /// <summary>Sırada yinelenecek işlemin türü; yoksa <see langword="null"/>.</summary>
    public EditScope? NextRedo => _redo.Count > 0 ? _redo[^1] : null;

    /// <summary>Yeni bir işlem kaydeder ve yineleme geçmişini geçersiz kılar.</summary>
    public void Record(EditScope scope)
    {
        _undo.Add(scope);
        _redo.Clear();

        if (_undo.Count > _limit)
            _undo.RemoveAt(0);
    }

    /// <summary>Sıradaki işlemi geri alma listesinden yineleme listesine taşır.</summary>
    public EditScope? PopUndo()
    {
        if (_undo.Count == 0)
            return null;

        var scope = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(scope);
        return scope;
    }

    /// <summary>Sıradaki işlemi yineleme listesinden geri alma listesine taşır.</summary>
    public EditScope? PopRedo()
    {
        if (_redo.Count == 0)
            return null;

        var scope = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(scope);
        return scope;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
