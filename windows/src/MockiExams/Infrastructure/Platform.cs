using System.Runtime.InteropServices;

namespace MockiExams;

static class Platform
{
    /// <summary>True when the Windows build runs under Wine (for example CrossOver on a Mac).</summary>
    public static bool IsWine { get; } = OperatingSystem.IsWindows()
        && NativeLibrary.TryLoad("ntdll.dll", out var ntdll)
        && NativeLibrary.TryGetExport(ntdll, "wine_get_version", out _);
}
