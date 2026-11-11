using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Interop;

internal static class LocalTcpListener
{
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(nint table, ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint Owner;
    }

    public static uint? FindOwner(int port)
    {
        const int ipv4 = 2, ownerPidListeners = 3;
        const uint insufficientBuffer = 122;
        int size = 0;
        if (GetExtendedTcpTable(0, ref size, false, ipv4, ownerPidListeners, 0) != insufficientBuffer)
            return null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int capacity = size;
            if (capacity < sizeof(uint)) return null;
            nint table = Marshal.AllocHGlobal(capacity);
            try
            {
                uint result = GetExtendedTcpTable(table, ref size, false, ipv4, ownerPidListeners, 0);
                if (result == insufficientBuffer) continue;
                if (result != 0) return null;

                int rowSize = Marshal.SizeOf<TcpRow>();
                int count = Marshal.ReadInt32(table);
                if (count < 0 || count > (capacity - sizeof(uint)) / rowSize) return null;
                uint? owner = null;
                for (int index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<TcpRow>(table + sizeof(uint) + index * rowSize);
                    if ((row.LocalAddress == 0 || row.LocalAddress == 0x0100007f) &&
                        BinaryPrimitives.ReverseEndianness((ushort)row.LocalPort) == port)
                    {
                        if (owner.HasValue && owner != row.Owner) return null;
                        owner = row.Owner;
                    }
                }
                return owner;
            }
            finally { Marshal.FreeHGlobal(table); }
        }
        return null;
    }
}
