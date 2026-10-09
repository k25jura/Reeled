using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Reeled.Installer;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            string currentExeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
            bool isUninstall = args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(a, "/u", StringComparison.OrdinalIgnoreCase)) ||
                               string.Equals(currentExeName, "uninstall", StringComparison.OrdinalIgnoreCase);

            bool isStaged = args.Any(a => string.Equals(a, "--staged", StringComparison.OrdinalIgnoreCase));

            // If this is uninstallation and we are running from an installation directory (not staged in %TEMP%),
            // stage ourselves into %TEMP% to prevent file-locking and WinUI 3 XBF/PRI resource collisions with Reeled.exe.
            if (isUninstall && !isStaged)
            {
                string currentProcessPath = Environment.ProcessPath ?? "";
                string currentDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

                string targetDir = string.Empty;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].StartsWith("--dir=", StringComparison.OrdinalIgnoreCase))
                    {
                        targetDir = args[i].Substring(6).Trim('"');
                        break;
                    }
                    if (args[i].StartsWith("/dir=", StringComparison.OrdinalIgnoreCase))
                    {
                        targetDir = args[i].Substring(5).Trim('"');
                        break;
                    }
                    if (args[i].Equals("--dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        targetDir = args[i + 1].Trim('"');
                        break;
                    }
                }

                if (string.IsNullOrEmpty(targetDir))
                {
                    if (File.Exists(Path.Combine(currentDir, "Reeled.exe")))
                    {
                        targetDir = currentDir;
                    }
                    else
                    {
                        try
                        {
                            using var regKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Reeled");
                            targetDir = regKey?.GetValue("InstallLocation") as string ?? string.Empty;
                        }
                        catch { }

                        if (string.IsNullOrEmpty(targetDir))
                        {
                            targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Reeled");
                        }
                    }
                }

                string stageDir = Path.Combine(Path.GetTempPath(), "Reeled_Uninstall");
                try
                {
                    // Kill any leftover running processes from previous temp runs before copying
                    foreach (var p in Process.GetProcessesByName("Uninstall"))
                    {
                        try
                        {
                            if (p.MainModule?.FileName?.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) == true)
                            {
                                p.Kill();
                                p.WaitForExit(1000);
                            }
                        }
                        catch { }
                    }

                    if (!Directory.Exists(stageDir))
                    {
                        Directory.CreateDirectory(stageDir);
                    }

                    string stagedExe = Path.Combine(stageDir, "Uninstall.exe");
                    if (!string.IsNullOrEmpty(currentProcessPath) && File.Exists(currentProcessPath))
                    {
                        File.Copy(currentProcessPath, stagedExe, overwrite: true);
                    }

                    bool isSilent = args.Any(a => string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(a, "-silent", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(a, "/s", StringComparison.OrdinalIgnoreCase));

                    string stagedArguments = $"/uninstall --staged --dir=\"{targetDir}\"";
                    if (isSilent) stagedArguments += " /silent";

                    var psi = new ProcessStartInfo
                    {
                        FileName = stagedExe,
                        Arguments = stagedArguments,
                        UseShellExecute = true,
                        WorkingDirectory = stageDir
                    };

                    Process.Start(psi);
                    return; // Exit immediately! Frees install folder and prevents WinUI 3 resource conflict
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "reeled_installer_crash.log"),
                            $"[SelfStageError] {DateTime.Now}: {ex}\n");
                    }
                    catch { }
                }
            }

            // Normal WinUI 3 startup
            global::WinRT.ComWrappersSupport.InitializeComWrappers();
            global::Microsoft.UI.Xaml.Application.Start((p) => {
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "reeled_installer_crash.log"),
                    $"[StartupError] {DateTime.Now}: {ex}\nStackTrace:\n{ex.StackTrace}\n");
            }
            catch { }
            throw;
        }
    }
}
