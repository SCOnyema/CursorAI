using System.Diagnostics;
using System.IO;

namespace CursorAI.App;

internal static class DevelopmentConsole
{
    internal static void TryAttachToParent()
    {
        if (!NativeMethods.TryAttachToParentConsole())
        {
            return;
        }

        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"Could not initialize development console output: {exception.Message}");
        }
    }
}
