using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Lones.SptManager.Native;

public static class NtfsLinks
{
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const int FsctlSetReparsePoint = 0x000900A4;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int ErrorPrivilegeNotHeld = 1314;

    public static void CreateHardLink(string linkPath, string existingFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(existingFile);
        if (!NativeMethods.CreateHardLink(linkPath, existingFile, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateHardLink failed.");
        }
    }

    public static void CreateJunction(string junctionDirectory, string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(junctionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var junctionFull = Path.GetFullPath(junctionDirectory);
        var targetFull = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(targetFull))
        {
            throw new DirectoryNotFoundException(targetFull);
        }

        if (Directory.Exists(junctionFull) || File.Exists(junctionFull))
        {
            throw new IOException($"Junction path must not already exist: {junctionFull}");
        }

        Directory.CreateDirectory(junctionFull);
        try
        {
            SetMountPoint(junctionFull, targetFull);
        }
        catch
        {
            try
            {
                Directory.Delete(junctionFull);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the empty directory we created.
            }

            throw;
        }
    }

        public static bool IsJunction(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            try
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.Directory) != 0
                       && (attrs & FileAttributes.ReparsePoint) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }

        public static string? TryGetJunctionTarget(string path)
        {
            if (!IsJunction(path))
            {
                return null;
            }

            var info = new DirectoryInfo(path);
            var resolved = info.ResolveLinkTarget(returnFinalTarget: false);
            if (resolved is not null)
            {
                return Path.GetFullPath(resolved.FullName);
            }

            var raw = info.LinkTarget;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (raw.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                raw = raw[4..];
            }

            try
            {
                return Path.GetFullPath(raw);
            }
            catch (Exception)
            {
                return raw;
            }
        }

        /// <summary>
        /// Deletes the junction only (non-recursive). Never walks into the target.
        /// </summary>
        public static void RemoveJunction(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var full = Path.GetFullPath(path);
            if (!IsJunction(full))
            {
                throw new InvalidOperationException($"Not a junction: {full}");
            }

            Directory.Delete(full);
        }

        public static FileIdentity GetFileIdentity(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            using var handle = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!NativeMethods.GetFileInformationByHandle(handle.SafeFileHandle, out var info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFileInformationByHandle failed.");
            }

            var fileId = ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow;
            return new FileIdentity(info.dwVolumeSerialNumber, fileId, info.nNumberOfLinks);
        }

    private static void SetMountPoint(string junctionFull, string targetFull)
    {
        var substitute = @"\??\" + targetFull.TrimEnd('\\');
        var print = targetFull.TrimEnd('\\');
        var substituteBytes = System.Text.Encoding.Unicode.GetBytes(substitute + "\0");
        var printBytes = System.Text.Encoding.Unicode.GetBytes(print + "\0");

        var pathBuffer = new byte[substituteBytes.Length + printBytes.Length];
        Buffer.BlockCopy(substituteBytes, 0, pathBuffer, 0, substituteBytes.Length);
        Buffer.BlockCopy(printBytes, 0, pathBuffer, substituteBytes.Length, printBytes.Length);

        var headerSize = 8 + 8; // tag/data/reserved + four ushorts
        var reparseDataLength = (ushort)(8 + pathBuffer.Length);
        var buffer = new byte[headerSize + pathBuffer.Length];
        var offset = 0;
        WriteUInt32(buffer, ref offset, IoReparseTagMountPoint);
        WriteUInt16(buffer, ref offset, reparseDataLength);
        WriteUInt16(buffer, ref offset, 0);
        WriteUInt16(buffer, ref offset, 0);
        WriteUInt16(buffer, ref offset, (ushort)(substituteBytes.Length - 2));
        WriteUInt16(buffer, ref offset, (ushort)substituteBytes.Length);
        WriteUInt16(buffer, ref offset, (ushort)(printBytes.Length - 2));
        Buffer.BlockCopy(pathBuffer, 0, buffer, offset, pathBuffer.Length);

        var handle = NativeMethods.CreateFile(
            junctionFull,
            GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile for junction failed.");
        }

        try
        {
            if (!NativeMethods.DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorPrivilegeNotHeld)
                {
                    throw new InvalidOperationException("Creating a junction failed because a privilege is missing. Standard users can create junctions when they can create an empty directory.");
                }

                throw new Win32Exception(error, "FSCTL_SET_REPARSE_POINT failed. The source directory must be empty.");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static void WriteUInt16(byte[] buffer, ref int offset, ushort value)
    {
        buffer[offset++] = (byte)value;
        buffer[offset++] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] buffer, ref int offset, uint value)
    {
        buffer[offset++] = (byte)value;
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)(value >> 16);
        buffer[offset++] = (byte)(value >> 24);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            int dwIoControlCode,
            byte[] lpInBuffer,
            int nInBufferSize,
            IntPtr lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileInformationByHandle(
            Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
            out ByHandleFileInformation lpFileInformation);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ByHandleFileInformation
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }
}

public readonly record struct FileIdentity(uint VolumeSerial, ulong FileId, uint LinkCount);
