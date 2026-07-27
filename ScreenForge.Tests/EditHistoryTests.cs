using ScreenForge.Gif.Editing;

namespace ScreenForge.Tests;

public sealed class EditHistoryTests
{
    [Fact]
    public void Empty_HasNothingToUndo()
    {
        var history = new EditHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.PopUndo());
    }

    [Fact]
    public void Undo_FollowsChronologicalOrder()
    {
        var history = new EditHistory();

        history.Record(EditScope.Frames);
        history.Record(EditScope.Annotation);

        // En son çizim yapıldı; önce o geri alınmalı.
        Assert.Equal(EditScope.Annotation, history.PopUndo());
        Assert.Equal(EditScope.Frames, history.PopUndo());
        Assert.Null(history.PopUndo());
    }

    [Fact]
    public void Undo_HandlesInterleavedEdits()
    {
        var history = new EditHistory();

        history.Record(EditScope.Annotation);
        history.Record(EditScope.Frames);
        history.Record(EditScope.Annotation);
        history.Record(EditScope.Frames);

        Assert.Equal(EditScope.Frames, history.PopUndo());
        Assert.Equal(EditScope.Annotation, history.PopUndo());
        Assert.Equal(EditScope.Frames, history.PopUndo());
        Assert.Equal(EditScope.Annotation, history.PopUndo());
    }

    [Fact]
    public void NextUndo_PeeksWithoutConsuming()
    {
        var history = new EditHistory();
        history.Record(EditScope.Frames);

        Assert.Equal(EditScope.Frames, history.NextUndo);
        Assert.True(history.CanUndo);   // hâlâ orada
    }

    [Fact]
    public void Redo_ReversesUndoInOrder()
    {
        var history = new EditHistory();

        history.Record(EditScope.Frames);
        history.Record(EditScope.Annotation);

        history.PopUndo();
        history.PopUndo();

        Assert.Equal(EditScope.Frames, history.PopRedo());
        Assert.Equal(EditScope.Annotation, history.PopRedo());
    }

    [Fact]
    public void Record_InvalidatesRedoHistory()
    {
        var history = new EditHistory();

        history.Record(EditScope.Frames);
        history.PopUndo();
        Assert.True(history.CanRedo);

        // Geri alma sonrası yeni işlem yineleme zincirini koparır.
        history.Record(EditScope.Annotation);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void PopUndo_MakesEntryRedoable()
    {
        var history = new EditHistory();
        history.Record(EditScope.Annotation);

        history.PopUndo();

        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal(EditScope.Annotation, history.NextRedo);
    }

    [Fact]
    public void PopRedo_MakesEntryUndoableAgain()
    {
        var history = new EditHistory();
        history.Record(EditScope.Frames);
        history.PopUndo();

        history.PopRedo();

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void History_DropsOldestBeyondLimit()
    {
        var history = new EditHistory(limit: 2);

        history.Record(EditScope.Frames);
        history.Record(EditScope.Annotation);
        history.Record(EditScope.Frames);

        Assert.NotNull(history.PopUndo());
        Assert.NotNull(history.PopUndo());
        Assert.Null(history.PopUndo());
    }

    [Fact]
    public void Clear_ResetsBothDirections()
    {
        var history = new EditHistory();
        history.Record(EditScope.Frames);
        history.PopUndo();

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }
}
