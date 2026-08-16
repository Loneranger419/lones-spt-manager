using System.Runtime.InteropServices;
using System.Text;

namespace Lones.SptManager.Native;

public sealed class VolumeIdReader : IVolumeIdReader
{
    public string? GetVolumeId(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var volumePath = new StringBuilder(260);
        if (!NativeMethods.GetVolumePathName(full, volumePath, (uint)volumePath.Capacity))
        {
            return null;
        }

        var volumeName = new StringBuilder(260);
        if (!NativeMethods.GetVolumeNameForVolumeMountPoint(volumePath.ToString(), volumeName, (uint)volumeName.Capacity))
        {
            return volumePath.ToString().TrimEnd('\\');
        }

        return volumeName.ToString().TrimEnd('\\');
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVolumePathName(string lpszFileName, StringBuilder lpszVolumePathName, uint cchBufferLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, uint cchBufferLength);
    }
}
