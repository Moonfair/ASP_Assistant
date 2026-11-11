using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Services;

internal static class LineupClipboard
{
    internal const int CannotOpenClipboard = unchecked((int)0x800401D0);
    private const int AccessDenied = 5;
    private const uint UnicodeText = 13;
    private const uint MoveableMemory = 0x0002;
    private const int Attempts = 8;

    internal static async Task CopyAsync(Window owner, string text)
    {
        var window = new WindowInteropHelper(owner).EnsureHandle();
        var bytes = Encoding.Unicode.GetBytes(text.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\0");
        var memory = GlobalAlloc(MoveableMemory, (nuint)bytes.Length);
        if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "GlobalAlloc failed");
        try
        {
            var buffer = GlobalLock(memory);
            if (buffer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "GlobalLock failed");
            try { Marshal.Copy(bytes, 0, buffer, bytes.Length); }
            finally { GlobalUnlock(memory); }

            await RunWithRetryAsync(() => WithOpenClipboard(window, () =>
            {
                if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastPInvokeError(), "EmptyClipboard failed");
                if (SetClipboardData(UnicodeText, memory) == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetClipboardData failed");
                // The system owns the buffer once SetClipboardData succeeds.
                memory = IntPtr.Zero;
            }));
        }
        finally { if (memory != IntPtr.Zero) GlobalFree(memory); }
    }

    internal static async Task<string> ReadAsync(Window owner, int maxTextLength = LineupShareService.MaxShareTextLength)
    {
        var window = new WindowInteropHelper(owner).EnsureHandle();
        var text = "";
        await RunWithRetryAsync(() => WithOpenClipboard(window, () =>
        {
            if (!IsClipboardFormatAvailable(UnicodeText)) return;
            var memory = GetClipboardData(UnicodeText);
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetClipboardData failed");
            var size = GlobalSize(memory);
            if (size == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "GlobalSize failed");
            var buffer = GlobalLock(memory);
            if (buffer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "GlobalLock failed");
            try
            {
                // Read one character past the format limit so the importer can report oversize text.
                var length = (int)Math.Min((ulong)(size / 2), (ulong)maxTextLength + 1);
                text = Marshal.PtrToStringUni(buffer, length)!;
                var terminator = text.IndexOf('\0');
                if (terminator >= 0) text = text[..terminator];
            }
            finally { GlobalUnlock(memory); }
        }));
        return text;
    }

    private static void WithOpenClipboard(IntPtr window, Action action)
    {
        if (!OpenClipboard(window))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == AccessDenied)
                throw new ExternalException("OpenClipboard was denied", CannotOpenClipboard);
            throw new Win32Exception(error, "OpenClipboard failed");
        }
        try { action(); }
        finally { CloseClipboard(); }
    }

    internal static async Task RunWithRetryAsync(Action operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { operation(); return; }
            catch (ExternalException ex) when (ex.ErrorCode == CannotOpenClipboard && attempt < Attempts)
            {
                await Task.Delay(90);
            }
            catch (ExternalException ex)
            {
                var code = ex is Win32Exception native ? $"Win32 {native.NativeErrorCode}" : $"HRESULT 0x{ex.ErrorCode:X8}";
                AppLogger.Warn("Clipboard", $"Could not access lineup clipboard: {ex.Message} ({code})");
                throw;
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
