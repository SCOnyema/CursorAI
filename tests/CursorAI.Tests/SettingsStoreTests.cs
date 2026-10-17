using System.IO;
using CursorAI.App.Configuration;

namespace CursorAI.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "CursorAI.Tests", Guid.NewGuid().ToString("N"));

    private SettingsStore Store => new(_directory);

    [Fact]
    public void Load_WhenNothingHasBeenSaved_ReturnsEmptyRatherThanThrowing()
    {
        UserSettings settings = Store.Load();

        Assert.Equal(UserSettings.Empty, settings);
        Assert.False(settings.HasAnyCredential);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        UserSettings saved = new("gemini-key-value", "openai-key-value", "openai", "gemini-3.5-flash");

        Store.Save(saved);
        UserSettings loaded = Store.Load();

        Assert.Equal(saved, loaded);
        Assert.True(loaded.HasAnyCredential);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsConsentAndOpenAiModel()
    {
        UserSettings saved = new("key", null, "openai", null, ConsentVersion: 1, OpenAiModel: "gpt-5.4-mini");

        Store.Save(saved);

        Assert.Equal(saved, Store.Load());
    }

    [Fact]
    public void Save_DoesNotWriteCredentialsAsPlaintext()
    {
        // The whole point of the DPAPI layer: a key must not be readable in the file.
        const string key = "a-very-recognisable-secret-key";
        Store.Save(new UserSettings(key, null, "gemini", null));

        string onDisk = File.ReadAllText(Store.FilePath);

        Assert.DoesNotContain(key, onDisk);
        Assert.Contains("GeminiApiKey", onDisk);
    }

    [Fact]
    public void Save_LeavesNonSecretFieldsReadable()
    {
        // Provider and model are not secrets, and being able to read them makes a misconfigured
        // install diagnosable by eye.
        Store.Save(new UserSettings(null, null, "openai", "gemini-3.5-flash"));

        string onDisk = File.ReadAllText(Store.FilePath);

        Assert.Contains("openai", onDisk);
        Assert.Contains("gemini-3.5-flash", onDisk);
    }

    [Fact]
    public void Save_OmitsFieldsThatAreBlank()
    {
        Store.Save(new UserSettings("key", "   ", null, ""));

        UserSettings loaded = Store.Load();

        Assert.Equal("key", loaded.GeminiApiKey);
        Assert.Null(loaded.OpenAiApiKey);
        Assert.Null(loaded.Provider);
        Assert.Null(loaded.GeminiModel);
    }

    [Fact]
    public void Save_TrimsStoredValues()
    {
        Store.Save(new UserSettings("  key  ", null, "  gemini  ", "  model  "));

        UserSettings loaded = Store.Load();

        Assert.Equal("key", loaded.GeminiApiKey);
        Assert.Equal("gemini", loaded.Provider);
        Assert.Equal("model", loaded.GeminiModel);
    }

    [Fact]
    public void Load_OnACorruptFile_ReturnsEmptyRatherThanPreventingStartup()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Store.FilePath, "{ this is not valid json");

        Assert.Equal(UserSettings.Empty, Store.Load());
    }

    [Fact]
    public void Load_OnAnUndecryptableCredential_DropsItWithoutThrowing()
    {
        // What happens when settings.json is copied from another machine or account: the blob
        // cannot be decrypted, and that must degrade rather than crash.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Store.FilePath,
            "{\"GeminiApiKey\":\"bm90LWEtdmFsaWQtZHBhcGktYmxvYg==\",\"Provider\":\"gemini\"}");

        UserSettings loaded = Store.Load();

        Assert.Null(loaded.GeminiApiKey);
        Assert.Equal("gemini", loaded.Provider);
    }

    [Fact]
    public void Save_OverwritesAPreviousFile()
    {
        Store.Save(new UserSettings("first", null, "gemini", null));
        Store.Save(new UserSettings("second", null, "gemini", null));

        Assert.Equal("second", Store.Load().GeminiApiKey);
    }

    [Fact]
    public void DefaultDirectory_IsUnderLocalAppData_NotBesideTheExecutable()
    {
        // An installed build may sit somewhere unwritable such as Program Files.
        string expected = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(expected, SettingsStore.DefaultDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("CursorAI", SettingsStore.DefaultDirectory);
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
