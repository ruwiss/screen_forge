using SkiaSharp;

namespace ScreenForge.Editor;

/// <summary>
/// Tuval dokümanı: arka plan (yakalanan görüntü ya da renk/gradient) + öğe listesi
/// (z-sıralı) + undo/redo geçmişi. Hem bölge editörü hem kolaj modu bunu kullanır.
/// </summary>
public sealed class Scene
{
    /// <summary>Öğeler, çizim sırası = z-sıra (son = en üstte).</summary>
    public List<SceneItem> Items { get; } = new();

    /// <summary>Tek arka plan görüntüsü (bölge/tam ekran editörü). Kolajda null olabilir.</summary>
    public SKBitmap? Background { get; set; }

    /// <summary>Arka plan boş olduğunda kullanılan tuval boyutu (kolaj).</summary>
    public SKSize CanvasSize { get; set; }

    /// <summary>Kolaj arka plan dolgusu (renk). Şeffaf = yok.</summary>
    public SKColor BackgroundColor { get; set; } = SKColors.Transparent;

    /// <summary>Adım numaralandırması için sayaç. Reset sonrası sıfırlanabilir.</summary>
    private int _stepCounter = -1;
    public int NextStepNumber
    {
        get
        {
            if (_stepCounter < 0)
                _stepCounter = Items.OfType<StepItem>().Select(s => s.Number).DefaultIfEmpty(0).Max();
            return ++_stepCounter;
        }
    }
    public void ResetStepCounter() => _stepCounter = 0;

    public float Width => Background?.Width ?? CanvasSize.Width;
    public float Height => Background?.Height ?? CanvasSize.Height;

    // ---- Undo/Redo ----
    private readonly Stack<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void ClearHistory() { _undo.Clear(); _redo.Clear(); }

    public event Action? Changed;

    /// <summary>Undo/Redo sonrası hangi öğelerin seçili olması gerektiğini bildirir (boş = seçimi temizle).</summary>
    public event Action<IReadOnlyList<SceneItem>>? SelectionRestore;

    public void Apply(IUndoableAction action)
    {
        action.Do(this);
        _undo.Push(action);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var a = _undo.Pop();
        a.Undo(this);
        _redo.Push(a);
        Changed?.Invoke();
        SelectionRestore?.Invoke(a.SelectAfterUndo(this));
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var a = _redo.Pop();
        a.Do(this);
        _undo.Push(a);
        Changed?.Invoke();
        SelectionRestore?.Invoke(a.SelectAfterDo(this));
    }

    public void RaiseChanged() => Changed?.Invoke();

    // ---- Z-sıra işlemleri ----
    public void BringToFront(SceneItem item)
    {
        if (Items.Remove(item)) Items.Add(item);
    }
    public void SendToBack(SceneItem item)
    {
        if (Items.Remove(item)) Items.Insert(0, item);
    }
    public void BringForward(SceneItem item)
    {
        int i = Items.IndexOf(item);
        if (i >= 0 && i < Items.Count - 1) { Items.RemoveAt(i); Items.Insert(i + 1, item); }
    }
    public void SendBackward(SceneItem item)
    {
        int i = Items.IndexOf(item);
        if (i > 0) { Items.RemoveAt(i); Items.Insert(i - 1, item); }
    }

    /// <summary>En üstteki öğeden başlayarak hit-test.</summary>
    public SceneItem? HitTest(SKPoint p)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
            if (Items[i].HitTest(p)) return Items[i];
        return null;
    }
}

// ===================== Undo/Redo eylemleri =====================

public interface IUndoableAction
{
    void Do(Scene scene);
    void Undo(Scene scene);
    /// <summary>Redo (Do) sonrası seçilecek öğeler. Varsayılan: hiçbiri.</summary>
    IReadOnlyList<SceneItem> SelectAfterDo(Scene scene) => System.Array.Empty<SceneItem>();
    /// <summary>Undo sonrası seçilecek öğeler. Varsayılan: hiçbiri.</summary>
    IReadOnlyList<SceneItem> SelectAfterUndo(Scene scene) => System.Array.Empty<SceneItem>();
}

/// <summary>Öğe ekleme.</summary>
public sealed class AddItemAction : IUndoableAction
{
    private readonly SceneItem _item;
    public AddItemAction(SceneItem item) => _item = item;
    public void Do(Scene s) => s.Items.Add(_item);
    public void Undo(Scene s) => s.Items.Remove(_item);
    // Redo: öğe geri eklendi → seç. Undo: öğe gitti → seçim yok.
    public IReadOnlyList<SceneItem> SelectAfterDo(Scene s) => new[] { _item };
}

/// <summary>Öğe silme (z-konumunu korur).</summary>
public sealed class RemoveItemAction : IUndoableAction
{
    private readonly SceneItem _item;
    private int _index;
    public RemoveItemAction(SceneItem item) => _item = item;
    public void Do(Scene s) { _index = s.Items.IndexOf(_item); s.Items.Remove(_item); }
    public void Undo(Scene s) { if (_index < 0) _index = s.Items.Count; s.Items.Insert(Math.Min(_index, s.Items.Count), _item); }
    // Undo: silinen öğe geri geldi → seç. Redo: silindi → seçim yok.
    public IReadOnlyList<SceneItem> SelectAfterUndo(Scene s) => new[] { _item };
}

/// <summary>Öğe durumunu (taşıma/boyut/stil) eski→yeni değiştirir.</summary>
public sealed class ModifyItemAction : IUndoableAction
{
    private readonly SceneItem _item;
    private readonly SceneItem _before;
    private readonly SceneItem _after;
    public ModifyItemAction(SceneItem item, SceneItem before, SceneItem after)
    {
        _item = item; _before = before; _after = after;
    }
    public void Do(Scene s) => _item.RestoreFrom(_after);
    public void Undo(Scene s) => _item.RestoreFrom(_before);
    // Taşıma/boyut/stil değişimi: her iki yönde de öğe seçili kalsın.
    public IReadOnlyList<SceneItem> SelectAfterDo(Scene s) => s.Items.Contains(_item) ? new[] { _item } : System.Array.Empty<SceneItem>();
    public IReadOnlyList<SceneItem> SelectAfterUndo(Scene s) => SelectAfterDo(s);
}

/// <summary>Birden fazla eylemi tek geri-al adımında gruplar (çoklu seçim işlemleri).</summary>
public sealed class CompositeAction : IUndoableAction
{
    private readonly List<IUndoableAction> _actions;
    public CompositeAction(IEnumerable<IUndoableAction> actions) => _actions = actions.ToList();
    public void Do(Scene s) { foreach (var a in _actions) a.Do(s); }
    public void Undo(Scene s) { for (int i = _actions.Count - 1; i >= 0; i--) _actions[i].Undo(s); }
    public IReadOnlyList<SceneItem> SelectAfterDo(Scene s) => _actions.SelectMany(a => a.SelectAfterDo(s)).Distinct().ToList();
    public IReadOnlyList<SceneItem> SelectAfterUndo(Scene s) => _actions.SelectMany(a => a.SelectAfterUndo(s)).Distinct().ToList();
}

/// <summary>Tüm sahneyi bir dikdörtgene kırpar: öğeleri ötelir, CanvasSize küçülür. Undo destekli.</summary>
public sealed class SceneCropAction : IUndoableAction
{
    private readonly SKRect _cropRect;
    private readonly SKSize _oldSize;
    private readonly List<SceneItem> _clonesBefore;

    public SceneCropAction(Scene scene, SKRect cropRect)
    {
        _cropRect = cropRect;
        _oldSize = scene.CanvasSize;
        _clonesBefore = scene.Items.Select(i => i.Clone()).ToList();
    }

    public void Do(Scene scene)
    {
        float dx = -_cropRect.Left, dy = -_cropRect.Top;
        scene.CanvasSize = new SKSize(_cropRect.Width, _cropRect.Height);
        foreach (var item in scene.Items) item.Move(dx, dy);
    }

    public void Undo(Scene scene)
    {
        scene.CanvasSize = _oldSize;
        for (int i = 0; i < scene.Items.Count && i < _clonesBefore.Count; i++)
            scene.Items[i].RestoreFrom(_clonesBefore[i]);
    }
}

/// <summary>Z-sıra değişimi (snapshot tabanlı, basit ve güvenli).</summary>
public sealed class ReorderAction : IUndoableAction
{
    private readonly List<SceneItem> _before;
    private readonly List<SceneItem> _after;
    public ReorderAction(List<SceneItem> before, List<SceneItem> after)
    {
        _before = before; _after = after;
    }
    public void Do(Scene s) { s.Items.Clear(); s.Items.AddRange(_after); }
    public void Undo(Scene s) { s.Items.Clear(); s.Items.AddRange(_before); }
}
