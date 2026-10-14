using System.Windows;
using System.Windows.Controls;
using CursorAI.App.Configuration;
using CursorAI.App.Vision;

namespace CursorAI.App.Ui;

/// <summary>
/// Lets a user supply credentials and pick a provider without a terminal or a rebuild.
/// </summary>
/// <remarks>
/// This is what makes CursorAI installable by anyone else. Before it, the only way to supply a
/// key was an environment variable in the launching shell, which a tester double-clicking an
/// executable has no way to set.
///
/// Where an environment variable is already set it stays authoritative, and this window names
/// the affected fields rather than silently accepting a value that will never take effect.
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;

    public SettingsWindow()
        : this(SettingsStore.Default)
    {
    }

    internal SettingsWindow(SettingsStore store)
    {
        _store = store;
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        UserSettings settings = AppConfiguration.Settings;
        GeminiKeyBox.Password = settings.GeminiApiKey ?? string.Empty;
        GeminiKeyPlain.Text = settings.GeminiApiKey ?? string.Empty;
        OpenAiKeyBox.Password = settings.OpenAiApiKey ?? string.Empty;
        ModelBox.Text = settings.GeminiModel ?? string.Empty;

        string provider = settings.Provider ?? VisionProviderFactory.DefaultProviderName;
        ProviderBox.SelectedIndex =
            provider.Equals("openai", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        WarnAboutEnvironmentOverrides();
    }

    /// <summary>
    /// Names any field the environment is already controlling. Without this, typing a key here
    /// while GEMINI_API_KEY is set would appear to do nothing at all.
    /// </summary>
    private void WarnAboutEnvironmentOverrides()
    {
        (string Variable, string Label)[] candidates =
        [
            ("GEMINI_API_KEY", "Gemini key"),
            ("OPENAI_API_KEY", "OpenAI key"),
            ("CURSORAI_VISION_PROVIDER", "Provider"),
            ("GEMINI_MODEL", "Model"),
        ];

        string[] overridden =
        [
            .. candidates
                .Where(c => AppConfiguration.IsOverriddenByEnvironment(c.Variable))
                .Select(c => $"{c.Label} ({c.Variable})")
        ];

        if (overridden.Length == 0)
        {
            return;
        }

        Report(
            "These are currently set by environment variables, which take precedence over "
            + $"anything saved here: {string.Join(", ", overridden)}.");
    }

    private void ShowKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        bool show = ShowKeyBox.IsChecked == true;
        if (show)
        {
            GeminiKeyPlain.Text = GeminiKeyBox.Password;
        }
        else
        {
            GeminiKeyBox.Password = GeminiKeyPlain.Text;
        }

        GeminiKeyPlain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        GeminiKeyBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string geminiKey = ShowKeyBox.IsChecked == true ? GeminiKeyPlain.Text : GeminiKeyBox.Password;
        string provider = (ProviderBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "gemini";

        UserSettings settings = new(
            Blank(geminiKey),
            Blank(OpenAiKeyBox.Password),
            provider,
            Blank(ModelBox.Text));

        try
        {
            _store.Save(settings);
            AppConfiguration.Update(settings);
            Console.WriteLine($"Settings saved to {_store.FilePath}");
            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            // Saving is the one thing this window exists to do, so a failure must be visible
            // here rather than only in a console the user may not have.
            Report($"Could not save settings: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Report(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
