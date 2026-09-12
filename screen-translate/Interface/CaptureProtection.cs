using System.Runtime.InteropServices;

namespace screen_translate.Interface;

/// <summary>Best-effort Windows defense against an app overlay entering a later desktop capture.</summary>
internal static class CaptureProtection
{
    private const uint WdaExcludeFromCapture = 0x00000011;

    public static bool TryExclude(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == 0) return false;
        try { return SetWindowDisplayAffinity(window, WdaExcludeFromCapture); }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
