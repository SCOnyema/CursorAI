using CursorAI.App.Configuration;
using CursorAI.App.Ui;

namespace CursorAI.Tests;

public class SettingsWindowTests
{
    [Fact]
    public void ApplyEdits_KeepsThePrivacyAgreement()
    {
        // Saving settings used to build a fresh record, which dropped the agreement and showed
        // the disclosure again on the next question. Every first run hit it: accept, paste key.
        UserSettings current = new(ConsentVersion: PrivacyConsent.CurrentVersion);

        UserSettings saved = SettingsWindow.ApplyEdits(current, "key", "", "gemini", "", "");

        Assert.True(PrivacyConsent.IsAccepted(saved));
    }

    [Fact]
    public void ApplyEdits_TakesEveryEditedFieldAndBlanksEmptyOnes()
    {
        UserSettings current = new("old-gemini", "old-openai", "gemini", "old-model", OpenAiModel: "old");

        UserSettings saved = SettingsWindow.ApplyEdits(
            current, " new-gemini ", "  ", "openai", "", " gpt-5.4-mini ");

        Assert.Equal("new-gemini", saved.GeminiApiKey);
        Assert.Null(saved.OpenAiApiKey);
        Assert.Equal("openai", saved.Provider);
        Assert.Null(saved.GeminiModel);
        Assert.Equal("gpt-5.4-mini", saved.OpenAiModel);
    }
}
