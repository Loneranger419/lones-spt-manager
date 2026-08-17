using System.Runtime.InteropServices;

namespace Lones.SptManager.Native;

/// <summary>
/// Console apps started from a GUI inherit a broken stdin and Windows Quick Edit.
/// Either one freezes SPT.Server until Enter.
/// </summary>
public static class ConsoleLaunch
{
    private const uint EnableQuickEdit = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const int StdInputHandle = -10;

    public static void TryDisableQuickEdit(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        NativeMethods.FreeConsole();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (NativeMethods.AttachConsole((uint)processId))
            {
                try
                {
                    var handle = NativeMethods.GetStdHandle(StdInputHandle);
                    if (handle == IntPtr.Zero || handle == -1)
                    {
                        return;
                    }

                    if (!NativeMethods.GetConsoleMode(handle, out var mode))
                    {
                        return;
                    }

                    mode &= ~EnableQuickEdit;
                    mode |= EnableExtendedFlags;
                    NativeMethods.SetConsoleMode(handle, mode);
                }
                finally
                {
                    NativeMethods.FreeConsole();
                }

                return;
            }

            Thread.Sleep(50);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
