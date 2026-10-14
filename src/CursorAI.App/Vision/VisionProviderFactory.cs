using CursorAI.App.Configuration;

namespace CursorAI.App.Vision;

/// <summary>
/// Chooses the vision provider at startup from the process environment, so switching is a
/// configuration change rather than a rebuild. This is explicit selection, never fallback:
/// exactly one provider is constructed, an unrecognised name is an error rather than a
/// silent default, and no provider is ever substituted for another at runtime.
/// </summary>
internal static class VisionProviderFactory
{
    internal const string ProviderEnvironmentVariable = "CURSORAI_VISION_PROVIDER";
    internal const string DefaultProviderName = "gemini";

    internal static IVisionProvider Create()
    {
        return Create(AppConfiguration.Resolve(
            ProviderEnvironmentVariable,
            static settings => settings.Provider));
    }

    internal static IVisionProvider Create(string? providerName)
    {
        string selected = string.IsNullOrWhiteSpace(providerName)
            ? DefaultProviderName
            : providerName.Trim().ToLowerInvariant();

        return selected switch
        {
            "gemini" => new GeminiVisionProvider(),
            "openai" => new OpenAIVisionProvider(),
            // One name for every endpoint that speaks the OpenAI chat-completions API:
            // OpenRouter, Groq, Together, Azure OpenAI, and local servers such as Ollama or
            // LM Studio. Which one is reached is a matter of configuration, not of code.
            "compatible" => new OpenAICompatibleVisionProvider(),
            _ => throw new InvalidOperationException(
                $"{ProviderEnvironmentVariable} is set to '{providerName}', which is not a known "
                + "vision provider. Valid values are 'gemini', 'openai' and 'compatible'. Leave "
                + $"it unset to use '{DefaultProviderName}'.")
        };
    }
}
