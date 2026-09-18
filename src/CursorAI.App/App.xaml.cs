using System.Windows;
using CursorAI.App.State;

namespace CursorAI.App;

public partial class App : Application
{
    public BuddyStateManager BuddyStateManager { get; } = new();
}
