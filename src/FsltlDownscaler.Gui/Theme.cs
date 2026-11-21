using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FsltlDownscaler.Gui;

/// <summary>Colour set for light and dark mode. Follows the Windows app setting.</summary>
internal sealed record Theme(
    bool Dark,
    Color Window,
    Color Card,
    Color CardTint,
    Color Border,
    Color Text,
    Color TextSecondary,
    Color TextMuted,
    Color Accent,
    Color AccentHover,
    Color OnAccent,
    Color AccentSoft,
    Color AccentSoftText,
    Color Track,
    Color Success,
    Color SuccessSoft,
    Color Warning,
    Color WarningSoft,
    Color Danger,
    Color ButtonHover)
{
    public static readonly Theme Light = new(
        Dark: false,
        Window: Color.FromArgb(245, 245, 242),
        Card: Color.White,
        CardTint: Color.FromArgb(240, 239, 234),
        Border: Color.FromArgb(218, 216, 209),
        Text: Color.FromArgb(31, 31, 30),
        TextSecondary: Color.FromArgb(95, 94, 90),
        TextMuted: Color.FromArgb(136, 135, 128),
        Accent: Color.FromArgb(24, 95, 165),
        AccentHover: Color.FromArgb(12, 68, 124),
        OnAccent: Color.White,
        AccentSoft: Color.FromArgb(230, 241, 251),
        AccentSoftText: Color.FromArgb(12, 68, 124),
        Track: Color.FromArgb(222, 220, 213),
        Success: Color.FromArgb(59, 109, 17),
        SuccessSoft: Color.FromArgb(234, 243, 222),
        Warning: Color.FromArgb(133, 79, 11),
        WarningSoft: Color.FromArgb(250, 238, 218),
        Danger: Color.FromArgb(163, 45, 45),
        ButtonHover: Color.FromArgb(236, 235, 230));

    public static readonly Theme DarkTheme = new(
        Dark: true,
        Window: Color.FromArgb(30, 30, 29),
        Card: Color.FromArgb(43, 43, 41),
        CardTint: Color.FromArgb(37, 37, 35),
        Border: Color.FromArgb(64, 64, 60),
        Text: Color.FromArgb(241, 239, 232),
        TextSecondary: Color.FromArgb(180, 178, 169),
        TextMuted: Color.FromArgb(136, 135, 128),
        Accent: Color.FromArgb(55, 138, 221),
        AccentHover: Color.FromArgb(81, 156, 230),
        OnAccent: Color.White,
        AccentSoft: Color.FromArgb(18, 50, 86),
        AccentSoftText: Color.FromArgb(181, 212, 244),
        Track: Color.FromArgb(58, 58, 55),
        Success: Color.FromArgb(151, 196, 89),
        SuccessSoft: Color.FromArgb(32, 48, 18),
        Warning: Color.FromArgb(239, 159, 39),
        WarningSoft: Color.FromArgb(56, 40, 12),
        Danger: Color.FromArgb(240, 149, 149),
        ButtonHover: Color.FromArgb(56, 56, 53));

    public static Theme Current => IsWindowsDark() ? DarkTheme : Light;

    public static bool IsWindowsDark()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int v && v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal static class NativeMethods
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

#pragma warning disable SYSLIB1054 // Plain DllImport keeps the project free of unsafe code.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
#pragma warning restore SYSLIB1054

    /// <summary>Dark or light title bar (Windows 10 20H1 and later). Ignored on older systems.</summary>
    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        try
        {
            int value = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not available: keep default title bar.
        }
    }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    /// <summary>Rounded corners and a border in the theme colour for popups (Windows 11). False on older systems.</summary>
    public static bool TryRoundCorners(IntPtr hwnd, Color border)
    {
        try
        {
            int preference = DWMWCP_ROUND;
            if (DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) != 0)
            {
                return false;
            }

            int colorRef = border.R | (border.G << 8) | (border.B << 16);
            _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Dark or light scroll bars for native controls such as the log box.</summary>
    public static void SetControlTheme(Control control, bool dark)
    {
        if (!control.IsHandleCreated)
        {
            return;
        }

        try
        {
            _ = SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not available: keep default look.
        }
    }
}
