using CursorAI.App.Configuration;

namespace CursorAI.Tests;

// Not run in parallel with anything else: AppConfiguration caches settings in static state,
// and these tests replace it.
[Collection("AppConfiguration")]
public class AppConfigurationTests
{
    private const string Variable = "CURSORAI_TESTS_RESOLVER";

    [Fact]
    public void Resolve_PrefersTheEnvironmentOverStoredSettings()
    {
        // Deliberate ordering: an existing development workflow must keep behaving exactly as
        // before once stored settings exist.
        AppConfiguration.Update(new UserSettings(GeminiApiKey: "from-settings"));
        try
        {
            Environment.SetEnvironmentVariable(Variable, "from-environment");

            Assert.Equal(
                "from-environment",
                AppConfiguration.Resolve(Variable, s => s.GeminiApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    [Fact]
    public void Resolve_FallsBackToStoredSettingsWhenTheEnvironmentIsUnset()
    {
        AppConfiguration.Update(new UserSettings(GeminiApiKey: "from-settings"));
        Environment.SetEnvironmentVariable(Variable, null);

        Assert.Equal("from-settings", AppConfiguration.Resolve(Variable, s => s.GeminiApiKey));
    }

    [Fact]
    public void Resolve_TreatsBlankEnvironmentValuesAsUnset()
    {
        AppConfiguration.Update(new UserSettings(GeminiApiKey: "from-settings"));
        try
        {
            Environment.SetEnvironmentVariable(Variable, "   ");

            Assert.Equal("from-settings", AppConfiguration.Resolve(Variable, s => s.GeminiApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    [Fact]
    public void Resolve_TrimsWhicheverSourceWins()
    {
        AppConfiguration.Update(new UserSettings(GeminiApiKey: "  padded-settings  "));
        Environment.SetEnvironmentVariable(Variable, null);
        Assert.Equal("padded-settings", AppConfiguration.Resolve(Variable, s => s.GeminiApiKey));

        try
        {
            Environment.SetEnvironmentVariable(Variable, "  padded-environment  ");
            Assert.Equal("padded-environment", AppConfiguration.Resolve(Variable, s => s.GeminiApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    [Fact]
    public void Resolve_WithNeitherSource_ReturnsNull()
    {
        AppConfiguration.Update(UserSettings.Empty);
        Environment.SetEnvironmentVariable(Variable, null);

        Assert.Null(AppConfiguration.Resolve(Variable, s => s.GeminiApiKey));
    }

    [Fact]
    public void IsOverriddenByEnvironment_ReportsWhatTheSettingsWindowMustWarnAbout()
    {
        Assert.False(AppConfiguration.IsOverriddenByEnvironment(Variable));
        try
        {
            Environment.SetEnvironmentVariable(Variable, "x");
            Assert.True(AppConfiguration.IsOverriddenByEnvironment(Variable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }
}
