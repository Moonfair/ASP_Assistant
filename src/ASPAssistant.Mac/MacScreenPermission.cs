using System.Runtime.InteropServices;

namespace ASPAssistant.Mac;

internal static class MacScreenPermission
{
    public static bool IsGranted => CGPreflightScreenCaptureAccess();
    public static bool Request() => CGRequestScreenCaptureAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestScreenCaptureAccess();
}
