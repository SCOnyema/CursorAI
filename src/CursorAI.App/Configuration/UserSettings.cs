namespace CursorAI.App.Configuration;

/// <summary>
/// Settings a user can change without a rebuild or a terminal.
/// </summary>
/// <param name="GeminiApiKey">Gemini credential, or null when not configured.</param>
/// <param name="OpenAiApiKey">OpenAI credential, used only when that provider is selected.</param>
/// <param name="Provider">"gemini" or "openai"; null means the built-in default.</param>
/// <param name="GeminiModel">Model override, or null for the built-in default.</param>
public sealed record UserSettings(
    string? GeminiApiKey = null,
    string? OpenAiApiKey = null,
    string? Provider = null,
    string? GeminiModel = null)
{
    public static UserSettings Empty { get; } = new();

    public bool HasAnyCredential =>
        !string.IsNullOrWhiteSpace(GeminiApiKey) || !string.IsNullOrWhiteSpace(OpenAiApiKey);
}
