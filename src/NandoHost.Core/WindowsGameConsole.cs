using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NandoHost.Core;

// Owned, invisible Windows pseudoconsole. Terraria's Windows console input is not
// equivalent to redirected stdin. This uses the OS ConPTY API without a shell.
internal sealed class WindowsGameConsole : IDisposable
{
    private IntPtr console;
    public Process Process { get; private set; } = null!;
    public StreamReader Output { get; private set; } = null!;
    public StreamWriter Input { get; private set; } = null!;
    private WindowsGameConsole() { }
    public static WindowsGameConsole Start(string executable, string[] arguments, string directory)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            throw new PlatformNotSupportedException("O console Terraria exige Windows 10 1809 ou superior.");
        var instance = new WindowsGameConsole();
        IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, outputRead = IntPtr.Zero, outputWrite = IntPtr.Zero;
        IntPtr attributes = IntPtr.Zero;
        bool initialized = false;
        try
        {
            if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) || !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = CreatePseudoConsole(new Coord { X = 140, Y = 40 }, inputRead, outputWrite, 0, out instance.console);
            Marshal.ThrowExceptionForHR(result);
            IntPtr bytes = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
            attributes = Marshal.AllocHGlobal(bytes);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x00020016), instance.console, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx { Attributes = attributes };
            startup.StartupInfo.Size = Marshal.SizeOf<StartupInfoEx>();
            // Prevent redirected std handles of the parent from bypassing ConPTY.
            startup.StartupInfo.Flags = 0x100; // STARTF_USESTDHANDLES, explicitly null std handles
            var command = new StringBuilder(string.Join(' ', new[] { executable }.Concat(arguments).Select(Quote)));
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero, directory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                instance.Process = Process.GetProcessById((int)info.Id);
                _ = instance.Process.SafeHandle; // retain a managed handle before releasing CreateProcess's handle
            }
            finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
            CloseHandle(inputRead); inputRead = IntPtr.Zero;
            CloseHandle(outputWrite); outputWrite = IntPtr.Zero;
            var output = new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read, 4096, false); outputRead = IntPtr.Zero;
            var input = new FileStream(new SafeFileHandle(inputWrite, true), FileAccess.Write, 4096, false); inputWrite = IntPtr.Zero;
            instance.Output = new StreamReader(output, new UTF8Encoding(false), false, 4096);
            // VT input uses CR as the Enter key, rather than pipe-style CRLF.
            instance.Input = new StreamWriter(input, new UTF8Encoding(false), 4096) { AutoFlush = true, NewLine = "\r" };
            return instance;
        }
        catch
        {
            try { if (instance.Process is { HasExited: false }) instance.Process.Kill(true); } catch (InvalidOperationException) { }
            instance.Output?.Dispose(); instance.Input?.Dispose();
            if (outputRead != IntPtr.Zero) { CloseHandle(outputRead); outputRead = IntPtr.Zero; }
            if (instance.console != IntPtr.Zero) { ClosePseudoConsole(instance.console); instance.console = IntPtr.Zero; }
            instance.Process?.Dispose();
            throw;
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            foreach (var handle in new[] { inputRead, inputWrite, outputRead, outputWrite }) if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }
    private static string Quote(string value)
    {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') result.Append('\\', slashes * 2 + 1).Append('"');
            else result.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    public async Task CloseAsync()
    {
        var handle = Interlocked.Exchange(ref console, IntPtr.Zero);
        // Keep Output draining on its independent reader while ConPTY closes.
        if (handle != IntPtr.Zero) await Task.Run(() => ClosePseudoConsole(handle));
    }
    public void Dispose()
    {
        Input?.Dispose(); Output?.Dispose();
        var handle = Interlocked.Exchange(ref console, IntPtr.Zero);
        if (handle != IntPtr.Zero) ClosePseudoConsole(handle); // output pipe already closed, cannot deadlock waiting for a reader
    }
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public IntPtr Reserved, Desktop, Title;
        public int X, Y, Width, Height, CountX, CountY, Fill, Flags;
        public short ShowWindow, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Id, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr security, uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
