using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Reeled.Launcher
{
    static class Program
    {
        [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONWARNING = 0x00000030;
        private const uint MB_ICONERROR = 0x00000010;

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string setupExe = Path.Combine(dir, "ReeledSetup.exe");
                if (!File.Exists(setupExe))
                {
                    setupExe = Path.Combine(dir, "..", "ReeledSetup.exe");
                }

                if (File.Exists(setupExe))
                {
                    string passArgs = "/uninstall";
                    if (args != null && args.Length > 0)
                    {
                        passArgs += " " + string.Join(" ", args);
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = setupExe,
                        Arguments = passArgs,
                        UseShellExecute = true,
                        WorkingDirectory = dir
                    };
                    Process.Start(psi);
                    return 0;
                }
                else
                {
                    MessageBox(
                        IntPtr.Zero,
                        "ReeledSetup.exe was not found in the application directory.\nPlease download the installer from GitHub to manage or uninstall Reeled.",
                        "Reeled Uninstaller",
                        MB_OK | MB_ICONWARNING);
                    return 1;
                }
            }
            catch (Exception ex)
            {
                MessageBox(
                    IntPtr.Zero,
                    "Failed to launch uninstaller: " + ex.Message,
                    "Reeled Uninstaller",
                    MB_OK | MB_ICONERROR);
                return 1;
            }
        }
    }
}
