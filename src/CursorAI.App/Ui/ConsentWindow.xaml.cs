using System.Windows;
using CursorAI.App.Configuration;

namespace CursorAI.App.Ui;

/// <summary>
/// Shows what leaves the machine and records the answer.
/// </summary>
/// <remarks>
/// Declining is a first-class outcome, not an error: local-only capture keeps working, so the
/// window never has to choose between informed agreement and a usable application.
/// </remarks>
public partial class ConsentWindow : Window
{
    private readonly SettingsStore _store;

    public ConsentWindow()
        : this(SettingsStore.Default)
    {
    }

    internal ConsentWindow(SettingsStore store)
    {
        _store = store;
        InitializeComponent();
        DisclosureText.Text = PrivacyConsent.DisclosureText;
    }

    /// <summary>True when the user agreed; false when they declined or closed the window.</summary>
    public bool Accepted { get; private set; }

    private void AcceptButton_Click(object sender, RoutedEventArgs e) => Record(accepted: true);

    private void DeclineButton_Click(object sender, RoutedEventArgs e) => Record(accepted: false);

    private void Record(bool accepted)
    {
        Accepted = accepted;
        UserSettings updated = AppConfiguration.Settings with
        {
            ConsentVersion = accepted ? PrivacyConsent.CurrentVersion : null
        };

        try
        {
            _store.Save(updated);
            AppConfiguration.Update(updated);
        }
        catch (Exception exception)
        {
            // Failing to persist the answer must not block the session. Being asked again next
            // launch is the right failure mode for a consent prompt.
            Console.Error.WriteLine(
                $"Could not record the privacy choice: {exception.GetType().Name}: {exception.Message}");
            AppConfiguration.Update(updated);
        }

        Console.WriteLine(accepted
            ? "Privacy disclosure accepted; screen analysis is enabled."
            : "Privacy disclosure declined; running in local-only mode.");

        DialogResult = accepted;
        Close();
    }
}
