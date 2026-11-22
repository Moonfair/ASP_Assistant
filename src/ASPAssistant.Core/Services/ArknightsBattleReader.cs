using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ASPAssistant.Core.Services;

public enum BattleFeedState
{
    Connecting, WaitingForClient, WindowsOnly, WaitingForRound, Live, PermissionRequired,
    UnsupportedBuild, ReadInterrupted, ConnectionFailed
}

public sealed record BattleUpdate(BattleFeedState State, BattleFrame? Sample = null,
    GarrisonSnapshot? Garrison = null, bool GarrisonInterrupted = false, GameRoomObservation? Room = null);

/// <summary>Reads the fixed-step clock and character counters of the verified Windows 2.7.91 build.</summary>
public sealed class ArknightsBattleReader : IDisposable
{
    private const string ExeHash = "5F03C3C7260668B9929B5E0FA85087D318F2529E87EFEDC77769EF7A91615993";
    private const string AssemblyHash = "CF7956BF88A61986859EF55C026D231713E2D12CF69FFD9A19779AB491CF76FF";
    private const string MetadataHash = "E58FB854AE519BC22F7D8BC7AC83502425A51B896A907A3374F5215202F9CAA5";
    // Two IL2CPP type references and the fields used below were verified on this exact x64 image.
    private const int BattleTypeRva = 145816640, SingletonTypeRva = 148525152, ImageSize = 226140160;
    private const int ClassParent = 88, StaticFields = 184, NativeObject = 16;
    private const int BattleLogger = 224, Level = 352, State = 552, Frame = 20, PlayTime = 24;
    private const int LoggerOwner = 24, LoggerStats = 48, CharacterRows = 48, DamageByType = 56;
    private const int ListItems = 16, ArrayLength = 24, ArrayData = 32;

    private readonly SafeProcessHandle _process;
    private readonly long _module;
    private readonly Dictionary<long, (string Name, string Namespace)> _classes = [];
    private record struct BattleIdentity(long Controller, long Native, long Logger, long Stats, long Level);
    private BattleIdentity? _lastIdentity;
    private uint _lastFrame;
    private double _lastTime;
    private int _round;

    private ArknightsBattleReader(SafeProcessHandle process, long module) => (_process, _module) = (process, module);

    public static ArknightsBattleReader? Open(uint expectedWindowPid, out BattleFeedState state)
    {
        state = BattleFeedState.WaitingForClient;
        var target = GameTargetSelection.FindWindowsClient();
        if (target == null || target.WindowPid != expectedWindowPid || !target.HasLiveWindow()) return null;
        state = BattleFeedState.UnsupportedBuild;
        if (!Environment.Is64BitProcess) return null;
        // Keep query and read access separate so a denied read is actionable only for the official game.
        using var query = OpenProcess(0x1000, false, expectedWindowPid);
        if (query.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var length = 1024;
        var image = new StringBuilder(length);
        if (!QueryFullProcessImageName(query, 0, image, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = image.ToString();
        if (!Path.GetFileName(path).Equals("Arknights.exe", StringComparison.OrdinalIgnoreCase)) return null;
        var folder = Path.GetDirectoryName(path)!;
        string assembly = Path.Combine(folder, "GameAssembly.dll");
        string metadata = Path.Combine(folder, "Arknights_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        if (!Matches(path, ExeHash) || !Matches(assembly, AssemblyHash) || !Matches(metadata, MetadataHash)) return null;

        var handle = OpenProcess(0x1010, false, expectedWindowPid);
        try
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            using var process = Process.GetProcessById(checked((int)expectedWindowPid));
            var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(m =>
                string.Equals(m.FileName, assembly, StringComparison.OrdinalIgnoreCase));
            if (module?.ModuleMemorySize != ImageSize || !target.HasLiveWindow()) return null;
            var reader = new ArknightsBattleReader(handle, module.BaseAddress.ToInt64());
            handle = null; // Ownership passes to the bound reader.
            state = BattleFeedState.WaitingForRound;
            return reader;
        }
        finally { handle?.Dispose(); }
    }

    private static bool Matches(string path, string hash)
    {
        if (!File.Exists(path)) return false;
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(file)) == hash;
    }

    public bool HasExited => !GetExitCodeProcess(_process, out uint exit) || exit != 259;

    public BattleUpdate Read()
    {
        var battle = ReadBattle();
        var room = new GameRoomMemoryReader(_module, Bytes, TypeMatches, GameIdentifier);
        battle = battle with { Room = room.Read() };
        try
        {
            var garrison = new GarrisonMemoryReader(Bytes, IsObject, GameIdentifier);
            return battle with { Garrison = garrison.Read(FindController) };
        }
        catch (InvalidDataException) { return battle with { GarrisonInterrupted = true }; }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 299 or 487 or 998)
        { return battle with { GarrisonInterrupted = true }; }
    }

    private BattleUpdate ReadBattle()
    {
        // A tick may advance during a read. Retry the entire snapshot rather than mixing frames.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return ReadFrame(); }
            catch (InvalidDataException) { }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 299 or 487 or 998) { }
        }
        return new(BattleFeedState.ReadInterrupted);
    }

    private (long Slot, long Controller) FindController()
    {
        var type = Long(_module + BattleTypeRva);
        var parent = Long(_module + SingletonTypeRva);
        if (IsPointer(type))
        {
            CheckType(type, "BattleController", "Torappu.Battle");
            var declaredParent = Long(type + ClassParent);
            Require(!IsPointer(parent) || parent == declaredParent);
            parent = declaredParent;
        }
        if (!IsPointer(parent)) return default;
        CheckType(parent, "SingletonMonoBehaviour`1", "Torappu");
        var singleton = Long(parent + StaticFields);
        if (singleton == 0) return default;
        var controller = Long(singleton);
        if (controller == 0) return default;
        var klass = ObjectType(controller, "BattleController", "Torappu.Battle");
        Require((!IsPointer(type) || klass == type) && Long(klass + ClassParent) == parent);
        return (singleton, controller);
    }

    private BattleUpdate ReadFrame()
    {
        var (singleton, controller) = FindController();
        if (controller == 0) return Waiting();
        var klass = Long(controller);
        var native = Long(controller + NativeObject);
        var logger = Long(controller + BattleLogger);
        if (native == 0 || logger == 0) return Waiting();
        ObjectType(logger, "BattleLogger", "Torappu.Battle");
        Require(IsPointer(native) && Long(logger + LoggerOwner) == controller);
        var stats = Long(logger + LoggerStats);
        if (stats == 0) return Waiting();
        ObjectType(stats, "BattleStats", "");
        var rows = Long(stats + CharacterRows);
        if (rows == 0) return Waiting();
        ObjectType(rows, "ListDict`2", "Torappu");

        var identity = new BattleIdentity(controller, native, logger, stats, Long(controller + Level));
        var statics = Long(klass + StaticFields);
        Require(IsPointer(statics));
        var tick = Bytes(statics + Frame, 12);
        var frame = BitConverter.ToUInt32(tick);
        var seconds = BitConverter.ToInt64(tick, PlayTime - Frame) / 4294967296d;
        var state = Int(controller + State);
        Require(seconds is >= 0 and < 259200 && state is >= 0 and <= 3);
        var head = Bytes(rows + ListItems, 16);
        var list = ListContents(head, 128);
        var counters = new Dictionary<string, OperatorCounters>(StringComparer.Ordinal);
        if (list.Count > 0)
        {
            var entries = Bytes(list.Items + ArrayData, list.Count * 16);
            for (var i = 0; i < list.Count; i++)
            {
                var id = OperatorKey(BitConverter.ToInt64(entries, i * 16));
                if (!id.StartsWith("char_", StringComparison.Ordinal)) continue;
                Require(counters.TryAdd(id, Counters(BitConverter.ToInt64(entries, i * 16 + 8))));
            }
        }
        Require(Long(singleton) == controller && Long(controller + NativeObject) == native
            && Long(controller + BattleLogger) == logger && Long(logger + LoggerOwner) == controller
            && Long(logger + LoggerStats) == stats && Long(controller + Level) == identity.Level
            && Long(stats + CharacterRows) == rows && Int(controller + State) == state
            && head.AsSpan().SequenceEqual(Bytes(rows + ListItems, 16))
            && tick.AsSpan().SequenceEqual(Bytes(statics + Frame, 12)));
        if (identity != _lastIdentity || frame < _lastFrame || seconds < _lastTime) _round++;
        (_lastIdentity, _lastFrame, _lastTime) = (identity, frame, seconds);
        return new(BattleFeedState.Live, new(_round, frame, seconds, state == 3, counters));
    }

    private BattleUpdate Waiting()
    {
        _lastIdentity = null;
        return new(BattleFeedState.WaitingForRound);
    }

    private OperatorCounters Counters(long row)
    {
        ObjectType(row, "CharAdvancedStats", "");
        var types = Long(row + DamageByType);
        ObjectType(types, "List`1", "System.Collections.Generic");
        var list = ListContents(Bytes(types + ListItems, 16), 6);
        Require(list.Count == 6);
        var values = Bytes(list.Items + ArrayData, 24);
        double At(int slot)
        {
            var value = BitConverter.ToSingle(values, 4 * slot);
            Require(value is >= 0 and <= 1e15f);
            return value;
        }
        return new(At(1), At(2), At(3), At(5));
    }

    private (long Items, int Count) ListContents(byte[] head, int maximum)
    {
        var items = BitConverter.ToInt64(head);
        var count = BitConverter.ToInt32(head, 8);
        Require(count >= 0 && count <= maximum);
        if (count > 0)
        {
            Require(IsPointer(items));
            var capacity = Long(items + ArrayLength);
            Require(capacity >= count && capacity <= 65536);
        }
        return (items, count);
    }

    private string OperatorKey(long obj)
    {
        var key = GameIdentifier(obj);
        Require(key.Length is > 0 and <= 110 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '#' or '.' or '-'));
        return key;
    }

    private string GameIdentifier(long obj)
    {
        ObjectType(obj, "String", "System");
        var length = Int(obj + 16);
        Require(length is >= 0 and <= 256);
        if (length == 0) return "";
        var key = Encoding.Unicode.GetString(Bytes(obj + 20, length * 2));
        Require(!key.Any(char.IsControl));
        return key;
    }

    private long ObjectType(long obj, string name, string ns)
    {
        Require(IsPointer(obj));
        var type = Long(obj);
        CheckType(type, name, ns);
        return type;
    }

    private void CheckType(long type, string name, string ns)
    {
        Require(TypeMatches(type, name, ns));
    }

    private bool IsObject(long obj, string name, string ns) =>
        IsPointer(obj) && TypeMatches(Long(obj), name, ns);

    private bool TypeMatches(long type, string name, string ns)
    {
        Require(IsPointer(type));
        if (_classes.TryGetValue(type, out var found)) return found == (name, ns);
        var names = Bytes(type + 16, 16);
        bool EqualsName(long p, string expected) =>
            Bytes(p, expected.Length + 1).AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(expected + "\0"));
        if (!EqualsName(BitConverter.ToInt64(names), name) || !EqualsName(BitConverter.ToInt64(names, 8), ns)) return false;
        _classes.Add(type, (name, ns));
        return true;
    }

    private static void Require(bool valid)
    {
        if (!valid) throw new InvalidDataException("Battle snapshot changed or its layout did not match.");
    }
    private static bool IsPointer(long p) => p is >= 0x10000 and < 0x7fffffffffff && (p & 7) == 0;
    private long Long(long address) => BitConverter.ToInt64(Bytes(address, 8));
    private int Int(long address) => BitConverter.ToInt32(Bytes(address, 4));
    private byte[] Bytes(long address, int count)
    {
        Require(count is >= 1 and <= 2048 && address >= 0x10000 && address < 0x7fffffffffff - count);
        var bytes = new byte[count];
        if (!ReadProcessMemory(_process, (nint)address, bytes, (nuint)count, out var read) || read != (nuint)count)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return bytes;
    }

    public void Dispose() => _process.Dispose();

    [DllImport("kernel32", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] data, nuint count, out nuint read);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
}
