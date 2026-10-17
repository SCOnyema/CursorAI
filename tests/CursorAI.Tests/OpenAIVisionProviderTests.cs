using CursorAI.App.Vision;
using OpenAI.Responses;

namespace CursorAI.Tests;

#pragma warning disable OPENAI001
public class OpenAIVisionProviderTests
{
    [Fact]
    public void BuildOptions_AsksForTheResponseNotToBeStored()
    {
        // The Responses API stores by default, and the privacy disclosure promises otherwise.
        CreateResponseOptions options = OpenAIVisionProvider.BuildOptions(
            "gpt-5.4-mini", "prompt", BinaryData.FromBytes([1, 2, 3], "image/png"));

        Assert.False(options.StoredOutputEnabled);
        Assert.Equal("gpt-5.4-mini", options.Model);
        Assert.Single(options.InputItems);
    }

    [Fact]
    public void RequestTimeout_MatchesTheOtherProviders()
    {
        Assert.Equal(GeminiVisionProvider.RequestTimeout, OpenAIVisionProvider.RequestTimeout);
        Assert.Equal(OpenAICompatibleVisionProvider.RequestTimeout, OpenAIVisionProvider.RequestTimeout);
    }
}
#pragma warning restore OPENAI001
