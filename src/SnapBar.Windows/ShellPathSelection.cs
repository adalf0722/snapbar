using System.IO;
using System.Runtime.InteropServices;
using SnapBar.Core;

namespace SnapBar.Windows;

// Use only inside an isolated worker: filesystem and Shell parsing may block.
// Construction, Reveal and Dispose must all run on that worker's same COM thread.
public sealed class ShellPathSelection : IDisposable
{
    private nint item;
    private bool initialized;
    private readonly string path;
    private readonly bool directory;
    private const uint FileSystemAttribute = 0x40000000;
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetFileAttributesW(string path);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(
        string name, nint context, out nint item, uint requested, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(nint item, uint count, nint children, uint flags);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public uint Size, Mask;
        public nint Window;
        [MarshalAs(UnmanagedType.LPWStr)] public string Verb;
        public nint File, Parameters, Directory;
        public int Show;
        public nint Instance, IdList, Class, ClassKey;
        public uint HotKey;
        public nint Icon, Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);

    public ShellPathSelection(string path)
    {
        var candidate = PathActionPolicy.Resolve(path);
        if (!candidate.Success) throw new ArgumentException("Invalid filesystem path");
        this.path = candidate.Path!;
        try
        {
            Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0));
            initialized = true;
            var fileAttributes = GetFileAttributesW(this.path);
            if (fileAttributes == uint.MaxValue) throw new FileNotFoundException("Item unavailable");
            directory = (fileAttributes & 0x10) != 0; // FILE_ATTRIBUTE_DIRECTORY
            Marshal.ThrowExceptionForHR(SHParseDisplayName(candidate.Path!, 0, out item, FileSystemAttribute, out var attributes));
            if (item == 0 || (attributes & FileSystemAttribute) == 0) throw new IOException("Not a filesystem item");
        }
        catch { Dispose(); throw; }
    }

    public string Reveal(TargetIdentity? target)
    {
        if (item == 0) return "path-not-prepared";
        if (directory)
        {
            // Never use a file's default verb, including if the folder was replaced
            // between prepare and reveal. Root folders use this same direct route.
            var attributes = GetFileAttributesW(path);
            if (attributes == uint.MaxValue || (attributes & 0x10) == 0) return "path-unavailable";
        }
        if (target == null || Native.GetIdentity(Native.GetForegroundWindow()) != target) return "target-changed";
        if (directory)
        {
            var info = new ShellExecuteInfo
            {
                Size = (uint)Marshal.SizeOf<ShellExecuteInfo>(),
                Mask = 0x00000004 | 0x00000100 | 0x00000400, // IDLIST | NOASYNC | FLAG_NO_UI
                Verb = "explore", IdList = item, Show = 1
            };
            if (!ShellExecuteExW(ref info)) return "explorer-unavailable";
        }
        else Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, 0, 0));
        return "ok";
    }

    public void Dispose()
    {
        if (item != 0) { Marshal.FreeCoTaskMem(item); item = 0; }
        if (initialized) { CoUninitialize(); initialized = false; }
    }
}
