using System.Runtime.InteropServices;
using System.Text;

namespace ASPAssistant.Mac;

internal static class MacKeychain
{
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("cn.moonfair.aspassistant.mac.lineup-site");
    private static readonly byte[] Account = Encoding.UTF8.GetBytes("session");
    private const int ItemNotFound = -25300;

    public static byte[]? Read()
    {
        var result = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)Account.Length, Account, out var length, out var data, out var item);
        if (result == ItemNotFound) return null;
        if (result != 0) throw new InvalidOperationException($"无法读取钥匙串 ({result})");
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, checked((int)length));
            return bytes;
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    public static void Write(byte[] data)
    {
        var result = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)Account.Length, Account, out _, out var existingData, out var existingItem);
        if (result == 0)
        {
            try
            {
                SecKeychainItemFreeContent(IntPtr.Zero, existingData);
                result = SecKeychainItemModifyAttributesAndData(existingItem, IntPtr.Zero,
                    (uint)data.Length, data);
                if (result != 0) throw new InvalidOperationException($"无法更新钥匙串登录信息 ({result})");
                return;
            }
            finally { if (existingItem != IntPtr.Zero) CFRelease(existingItem); }
        }
        if (result != ItemNotFound) throw new InvalidOperationException($"无法读取钥匙串登录信息 ({result})");
        result = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)Account.Length, Account, (uint)data.Length, data, out var item);
        if (item != IntPtr.Zero) CFRelease(item);
        if (result != 0) throw new InvalidOperationException($"无法保存钥匙串登录信息 ({result})");
    }

    public static void Delete()
    {
        var result = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)Account.Length, Account, out _, out var data, out var item);
        if (result == ItemNotFound) return;
        if (result != 0) throw new InvalidOperationException($"无法删除钥匙串登录信息 ({result})");
        try
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            result = SecKeychainItemDelete(item);
            if (result != 0) throw new InvalidOperationException($"无法删除钥匙串登录信息 ({result})");
        }
        finally { if (item != IntPtr.Zero) CFRelease(item); }
    }

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceNameLength,
        byte[] serviceName, uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength,
        byte[] serviceName, uint accountNameLength, byte[] accountName, uint passwordLength,
        byte[] passwordData, out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef,
        IntPtr attrList, uint length, byte[] data);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr handle);
}
