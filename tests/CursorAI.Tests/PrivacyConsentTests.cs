using System.IO;
using CursorAI.App.Configuration;

namespace CursorAI.Tests;

public class PrivacyConsentTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "CursorAI.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void IsAccepted_OnlyWhenTheStoredVersionMatchesTheCurrentOne()
    {
        Assert.False(PrivacyConsent.IsAccepted(UserSettings.Empty));
        Assert.True(PrivacyConsent.IsAccepted(
            UserSettings.Empty with { ConsentVersion = PrivacyConsent.CurrentVersion }));
    }

    [Fact]
    public void IsAccepted_RejectsAnOlderVersion_SoAChangeAsksAgain()
    {
        // The version exists so that changing what leaves the machine requires fresh agreement
        // rather than silently inheriting the old one.
        Assert.False(PrivacyConsent.IsAccepted(
            UserSettings.Empty with { ConsentVersion = PrivacyConsent.CurrentVersion - 1 }));
    }

    [Fact]
    public void DisclosureText_NamesEverythingThatIsSent()
    {
        string text = PrivacyConsent.DisclosureText;

        Assert.Contains("screenshot", text);
        Assert.Contains("cursor position", text);
        Assert.Contains("window title", text);
        Assert.Contains("your question", text);
    }

    [Fact]
    public void DisclosureText_IsExplicitThatAudioNeverLeaves()
    {
        Assert.Contains("audio", PrivacyConsent.DisclosureText);
        Assert.Contains("entirely on this machine", PrivacyConsent.DisclosureText);
    }

    [Fact]
    public void DisclosureText_WarnsThatAScreenshotCanContainAnything()
    {
        // The honest part: the user is agreeing to send whatever happens to be visible.
        string text = PrivacyConsent.DisclosureText;

        Assert.Contains("credentials", text);
        Assert.Contains("other people", text);
    }

    [Fact]
    public void DisclosureText_DoesNotOverclaimAboutProviderRetention()
    {
        // store=false is a statement about what CursorAI asks for, not a guarantee about the
        // provider's handling, and the disclosure must not blur that.
        string text = PrivacyConsent.DisclosureText;

        Assert.Contains("not a guarantee", text);
        Assert.Contains("data-use terms", text);
    }

    [Fact]
    public void DisclosureText_SaysDecliningKeepsLocalCaptureWorking()
    {
        Assert.Contains("local-only", PrivacyConsent.DisclosureText);
        Assert.Contains("nothing is", PrivacyConsent.DisclosureText);
    }

    [Fact]
    public void DisclosureText_SaysThereIsNoBackgroundCapture()
    {
        Assert.Contains("no background or periodic", PrivacyConsent.DisclosureText);
        Assert.Contains("no screen recording", PrivacyConsent.DisclosureText);
    }

    [Fact]
    public void ConsentChoice_SurvivesASaveAndLoadRoundTrip()
    {
        SettingsStore store = new(_directory);
        store.Save(UserSettings.Empty with { ConsentVersion = PrivacyConsent.CurrentVersion });

        Assert.True(PrivacyConsent.IsAccepted(store.Load()));
    }

    [Fact]
    public void DecliningIsStoredAsAbsence_SoItIsAskedAgain()
    {
        SettingsStore store = new(_directory);
        store.Save(UserSettings.Empty with { ConsentVersion = null, GeminiApiKey = "k" });

        UserSettings loaded = store.Load();

        Assert.False(PrivacyConsent.IsAccepted(loaded));
        Assert.Equal("k", loaded.GeminiApiKey);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
