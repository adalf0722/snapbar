using System.Globalization;
using System.Runtime.InteropServices;

namespace SnapBar.Windows;

public static class SystemUiLanguage
{
    // Windows' user display language is independent of the culture used by this process.
    public static string Read()
    {
        try { return CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).Name; }
        catch (CultureNotFoundException) { return "en"; }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}
