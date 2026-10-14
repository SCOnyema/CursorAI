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
    public void Create_SelectsTheCompatibleProvider_GivenItsRequiredConfiguration()
    {
        // One name for every endpoint speaking the OpenAI chat-completions API. Both values are
        // required because which models exist depends entirely on which endpoint is configured.
        try
        {
            Environment.SetEnvironmentVariable(
                OpenAICompatibleVisionProvider.BaseUrlEnvironmentVariable, "http://localhost:11434/v1");
            Environment.SetEnvironmentVariable(
                OpenAICompatibleVisionProvider.ModelEnvironmentVariable, "qwen2.5vl:7b");

            using IVisionProvider provider = VisionProviderFactory.Create("compatible");

            Assert.IsType<OpenAICompatibleVisionProvider>(provider);
            Assert.Contains("qwen2.5vl:7b", provider.Description);
            Assert.Contains("localhost:11434", provider.Description);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                OpenAICompatibleVisionProvider.BaseUrlEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(
                OpenAICompatibleVisionProvider.ModelEnvironmentVariable, null);
        }
    }

    [Fact]
    public void Create_CompatibleWithoutABaseUrl_SaysWhatIsMissing()
    {
        Environment.SetEnvironmentVariable(
            OpenAICompatibleVisionProvider.BaseUrlEnvironmentVariable, null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => VisionProviderFactory.Create("compatible"));

        Assert.Contains(OpenAICompatibleVisionProvider.BaseUrlEnvironmentVariable, exception.Message);
        Assert.Contains("openrouter.ai", exception.Message);
        Assert.Contains("localhost:11434", exception.Message);
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
        Assert.Contains("'compatible'", exception.Message);
    }

    [Fact]
    public void Create_WhitespaceSelection_IsTreatedAsUnset()
    {
        using IVisionProvider provider = VisionProviderFactory.Create("   ");

        Assert.IsType<GeminiVisionProvider>(provider);
    }
}
