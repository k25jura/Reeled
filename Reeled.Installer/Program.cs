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
            // stage ourselves into a unique folder in %TEMP% to prevent file-locking and WinUI 3 XBF/PRI resource collisions with Reeled.exe.
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

                // Opportunistically cleanup old completed staging folders
                try
                {
                    foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), "Reeled_Uninstall*"))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
                catch { }

                string stageDir = Path.Combine(Path.GetTempPath(), $"Reeled_Uninstall_{Guid.NewGuid():N}");
                try
                {
                    Directory.CreateDirectory(stageDir);

                    string stagedExe = Path.Combine(stageDir, "Uninstall.exe");
                    if (!string.IsNullOrEmpty(currentProcessPath) && File.Exists(currentProcessPath))
                    {
                        File.Copy(currentProcessPath, stagedExe, overwrite: false);
                    }

                    bool isSilent = args.Any(a => string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(a, "-silent", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(a, "/s", StringComparison.OrdinalIgnoreCase));

                    string safeTargetDir = targetDir.TrimEnd('\\', '/');
                    string stagedArguments = $"/uninstall --staged --dir=\"{safeTargetDir}\"";
                    if (isSilent) stagedArguments += " /silent";

                    var psi = new ProcessStartInfo
                    {
                        FileName = stagedExe,
                        Arguments = stagedArguments,
                        UseShellExecute = false,
                        WorkingDirectory = stageDir
                    };

                    var stagedProc = Process.Start(psi);
                    stagedProc?.WaitForExit();

                    if (stagedProc != null && stagedProc.ExitCode == 0)
                    {
                        // Uninstallation succeeded! Schedule cleanup of the installation directory
                        try
                        {
                            if (!string.IsNullOrEmpty(safeTargetDir) && Directory.Exists(safeTargetDir))
                            {
                                var cleanupPsi = new ProcessStartInfo
                                {
                                    FileName = "cmd.exe",
                                    Arguments = $"/c timeout /t 1 /nobreak > NUL & rmdir /s /q \"{safeTargetDir}\"",
                                    WindowStyle = ProcessWindowStyle.Hidden,
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                };
                                Process.Start(cleanupPsi);
                            }
                        }
                        catch { }
                    }

                    return;
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "reeled_installer_crash.log"),
                            $"[SelfStageError] {DateTime.Now}: {ex}\n");
                    }
                    catch { }
                    // NEVER drop through to WinUI 3 in the install directory!
                    return;
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
