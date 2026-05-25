using Avalonia.Controls;

namespace Kairo.Utils;

public static class Access
{
    // Reference to the main window for dialogs
    public static Window? MainWindow { get; set; }
    // Reference to dashboard window
    public static Window? DashBoard { get; set; }
}