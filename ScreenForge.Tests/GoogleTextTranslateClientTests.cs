using ScreenForge.Translate;

namespace ScreenForge.Tests;

public sealed class GoogleTextTranslateClientTests
{
    [Fact]
    public void ParseHtml_SampleFromRust()
    {
        var result = GoogleTextTranslateClient.ParseTranslateHtml("""[["Merhaba"],["ja"]]""");

        Assert.NotNull(result);
        Assert.Equal("Merhaba", result.Value.Text);
        Assert.Equal("ja", result.Value.SourceLang);
    }

    [Fact]
    public void ParsePa_SampleFromRust()
    {
        var result = GoogleTextTranslateClient.ParseTranslatePa(
            """{"translation":"Merhaba","sourceLanguage":"ja"}""");

        Assert.NotNull(result);
        Assert.Equal("Merhaba", result.Value.Text);
        Assert.Equal("ja", result.Value.SourceLang);
    }

    [Fact]
    public void ParseDict_SampleFromRust()
    {
        var result = GoogleTextTranslateClient.ParseDictChrome("""[["Merhaba","ja"]]""");

        Assert.NotNull(result);
        Assert.Equal("Merhaba", result.Value.Text);
        Assert.Equal("ja", result.Value.SourceLang);
    }

    [Fact]
    public void ParseGtx_SampleFromRust()
    {
        var result = GoogleTextTranslateClient.ParseGtx(
            """[[["Merhaba","こんにちは",null,null,10]],null,"ja"]""");

        Assert.NotNull(result);
        Assert.Equal("Merhaba", result.Value.Text);
        Assert.Equal("ja", result.Value.SourceLang);
    }

    [Fact]
    public void UnescapeHtml_Ampersand()
    {
        Assert.Equal("A & B", GoogleTextTranslateClient.UnescapeHtml("A &amp; B"));
    }

    [Fact]
    public void ParseDict_FlatRow()
    {
        var result = GoogleTextTranslateClient.ParseDictChrome("""["Merhaba","ja"]""");

        Assert.NotNull(result);
        Assert.Equal("Merhaba", result.Value.Text);
        Assert.Equal("ja", result.Value.SourceLang);
    }

    [Fact]
    public void ParseHtmlSegments_KeepsEachLine()
    {
        var lines = GoogleTextTranslateClient.ParseTranslateHtmlSegments(
            """[["Selam","İkinci"],["en","en"]]""", out string source);

        Assert.NotNull(lines);
        Assert.Equal(["Selam", "İkinci"], lines);
        Assert.Equal("en", source);
    }

    [Fact]
    public void JoinPreservingBlanks_RestoresEmptyLines()
    {
        string? joined = GoogleTextTranslateClient.JoinPreservingBlanks(
            "Hello\n\nSecond", ["Selam", "İkinci"]);

        Assert.Equal("Selam\n\nİkinci", joined);
    }

    [Fact]
    public void ParseHtml_UnescapesEntities()
    {
        var result = GoogleTextTranslateClient.ParseTranslateHtml("""[["A &amp; B"],["en"]]""");

        Assert.NotNull(result);
        Assert.Equal("A & B", result.Value.Text);
    }
}
