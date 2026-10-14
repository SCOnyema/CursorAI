using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CursorAI.App.Configuration;

/// <summary>
/// Reads and writes user settings, with credentials encrypted at rest.
/// </summary>
/// <remarks>
/// Until this existed the only way to supply an API key was an environment variable in the
/// launching shell, which makes the application unusable by anyone who is not running it from
/// a terminal they configured themselves.
///
/// Credentials are protected with DPAPI at <see cref="DataProtectionScope.CurrentUser"/>, so
/// the stored blob is useless to another account on the same machine and the plaintext key
/// never touches disk. That is meaningfully better than a plain JSON field and still not a
/// secret store: anything running as this user can decrypt it, which is inherent to a desktop
/// application holding a key it must use.
///
/// Settings live under %LOCALAPPDATA%, never beside the executable, because an installed build
/// may sit somewhere unwritable such as Program Files.
/// </remarks>
public sealed class SettingsStore
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directory;

    public SettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>The per-user location: %LOCALAPPDATA%\CursorAI.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorAI");

    public static SettingsStore Default { get; } = new(DefaultDirectory);

    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>
    /// Loads settings, returning <see cref="UserSettings.Empty"/> when absent or unreadable.
    /// A corrupt or undecryptable file must not stop the application from starting.
    /// </summary>
    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return UserSettings.Empty;
            }

            StoredSettings? stored = JsonSerializer.Deserialize<StoredSettings>(
                File.ReadAllText(FilePath), JsonOptions);

            return stored is null
                ? UserSettings.Empty
                : new UserSettings(
                    Unprotect(stored.GeminiApiKey),
                    Unprotect(stored.OpenAiApiKey),
                    stored.Provider,
                    stored.GeminiModel);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or CryptographicException)
        {
            Debug.WriteLine($"Could not read settings: {exception.GetType().Name}: {exception.Message}");
            return UserSettings.Empty;
        }
    }

    /// <summary>Writes settings, encrypting credentials. Throws so the UI can report failure.</summary>
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(_directory);

        StoredSettings stored = new(
            Protect(settings.GeminiApiKey),
            Protect(settings.OpenAiApiKey),
            Blank(settings.Provider),
            Blank(settings.GeminiModel));

        // Written through a temporary file so an interrupted save cannot leave a half-written
        // settings file that then fails to parse.
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored, JsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Protect(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        byte[] encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext.Trim()),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string? Unprotect(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
        {
            return null;
        }

        try
        {
            byte[] plaintext = ProtectedData.Unprotect(
                Convert.FromBase64String(encrypted),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // Copied from another machine or account: not recoverable, and not a crash.
            Debug.WriteLine($"Stored credential could not be decrypted: {exception.GetType().Name}");
            return null;
        }
    }

    /// <summary>On-disk shape. Credential fields hold DPAPI blobs, never plaintext.</summary>
    private sealed record StoredSettings(
        string? GeminiApiKey,
        string? OpenAiApiKey,
        string? Provider,
        string? GeminiModel);
}
