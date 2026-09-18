using System.Windows;

namespace CursorAI.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        const double screenMargin = 24;
        Left = SystemParameters.WorkArea.Right - Width - screenMargin;
        Top = SystemParameters.WorkArea.Bottom - Height - screenMargin;
    }
}
