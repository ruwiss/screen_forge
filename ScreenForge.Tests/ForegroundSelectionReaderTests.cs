using ScreenForge.Translate;

namespace ScreenForge.Tests;

public sealed class ForegroundSelectionReaderTests
{
    [Fact]
    public void Normalize_TrimsAndRejectsBlank()
    {
        Assert.Null(ForegroundSelectionReader.Normalize("   \n  "));
        Assert.Equal("Merhaba", ForegroundSelectionReader.Normalize("  Merhaba  "));
    }

    [Fact]
    public void Normalize_CapsVeryLongSelection()
    {
        string huge = new('a', ForegroundSelectionReader.MaxChars + 40);
        string? cut = ForegroundSelectionReader.Normalize(huge);

        Assert.NotNull(cut);
        Assert.Equal(ForegroundSelectionReader.MaxChars, cut!.Length);
    }
}
