using CursorAI.App.Vision;

namespace CursorAI.Tests;

public class VisionProviderFactoryTests
{
    [Fact]
    public void Create_WithNoSelection_UsesGemini()
    {
        using IVisionProvider provider = VisionProviderFactory.Create(null);

        Assert.IsType<GeminiVisionProvider>(provider);
        Assert.Contains("Gemini", provider.Description);
    }

    [Theory]
    [InlineData("gemini")]
    [InlineData("  GEMINI  ")]
    public void Create_SelectsGeminiCaseInsensitivelyAndTrimmed(string name)
    {
        using IVisionProvider provider = VisionProviderFactory.Create(name);

        Assert.IsType<GeminiVisionProvider>(provider);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData(" OpenAI ")]
    public void Create_SelectsOpenAi(string name)
    {
        using IVisionProvider provider = VisionProviderFactory.Create(name);

        Assert.IsType<OpenAIVisionProvider>(provider);
        Assert.Contains("OpenAI", provider.Description);
    }

    [Fact]
    public void Create_WithUnknownSelection_ThrowsRatherThanSilentlyDefaulting()
    {
        // Silently falling back would violate the project's no-automatic-fallback rule and
        // hide a typo as "it works but talks to the wrong vendor".
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => VisionProviderFactory.Create("claude"));

        Assert.Contains("CURSORAI_VISION_PROVIDER", exception.Message);
        Assert.Contains("claude", exception.Message);
        Assert.Contains("'gemini'", exception.Message);
        Assert.Contains("'openai'", exception.Message);
    }

    [Fact]
    public void Create_WhitespaceSelection_IsTreatedAsUnset()
    {
        using IVisionProvider provider = VisionProviderFactory.Create("   ");

        Assert.IsType<GeminiVisionProvider>(provider);
    }
}
