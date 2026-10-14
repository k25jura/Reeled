using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Reeled.Installer;

public static class Program
{
    private static readonly string TraceFile = Path.Combine(Path.GetTempPath(), "reeled_installer.log");

    internal static void Trace(string message)
    {
        try { File.AppendAllText(TraceFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}"); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("Microsoft.WindowsAppRuntime.dll", ExactSpelling = true)]
    private static extern int WindowsAppRuntime_EnsureIsLoaded();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    private static bool IsInJob()
    {
        try { return IsProcessInJob(Process.GetCurrentProcess().Handle, IntPtr.Zero, out bool r) && r; }
        catch { return false; }
    }

    /// <summary>
    /// Windows Settings > Apps runs uninstallers inside a job object that keeps WinUI windows from ever
    /// appearing (the process runs, but no window is created). Start the staged uninstaller outside that
    /// job: break away when the job allows it, otherwise let WMI create it (it is then parented to the
    /// WMI service, outside our job). Falls back to a normal child process if neither works.
    /// </summary>
    private static Process? StartOutsideJob(string exe, string arguments, string workingDirectory)
    {
        if (!IsInJob())
        {
            return Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, WorkingDirectory = workingDirectory });
        }

        string commandLine = $"\"{exe}\" {arguments}";

        // WMI first: the process is created by the WMI service, outside every job in our chain. Breakaway only
        // leaves the innermost job, and Settings nests one that still blocks the window.
        try
        {
            Type? locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType != null)
            {
                dynamic locator = Activator.CreateInstance(locatorType)!;
                dynamic service = locator.ConnectServer(".", @"root\cimv2");
                dynamic processClass = service.Get("Win32_Process");
                dynamic inParams = processClass.Methods_.Item("Create").InParameters.SpawnInstance_();
                inParams.CommandLine = commandLine;
                inParams.CurrentDirectory = workingDirectory;
                dynamic outParams = processClass.ExecMethod_("Create", inParams);
                int returnValue = (int)outParams.ReturnValue;
                int pid = (int)outParams.ProcessId;
                Trace($"WMI Win32_Process.Create returned {returnValue} pid={pid}");
                if (returnValue == 0)
                {
                    return TryGetProcess(pid);
                }
            }
        }
        catch (Exception ex)
        {
            Trace($"WMI launch failed: {ex.Message}");
        }

        var si = new STARTUPINFO { cb = System.Runtime.InteropServices.Marshal.SizeOf<STARTUPINFO>() };
        if (CreateProcess(null, new System.Text.StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                CREATE_BREAKAWAY_FROM_JOB | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, workingDirectory, ref si, out var pi))
        {
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            Trace($"Started outside job via breakaway pid={pi.dwProcessId}");
            return TryGetProcess(pi.dwProcessId);
        }
        Trace($"Breakaway not allowed (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");

        return Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, WorkingDirectory = workingDirectory });
    }

    private const uint SYNCHRONIZE = 0x00100000;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint INFINITE = 0xFFFFFFFF;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out int exitCode);

    private static int WaitForExitCode(int pid)
    {
        IntPtr handle = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            Trace($"OpenProcess({pid}) failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
            return -1;
        }
        try
        {
            WaitForSingleObject(handle, INFINITE);
            return GetExitCodeProcess(handle, out int code) ? code : -1;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static Process? TryGetProcess(int pid)
    {
        try { return Process.GetProcessById(pid); }
        catch { return null; }
    }

    private const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CreateProcess(string? lpApplicationName, System.Text.StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);


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

            Trace($"Start exe={Environment.ProcessPath} args=[{string.Join(" ", args)}] cwd={Environment.CurrentDirectory} " +
                  $"uninstall={isUninstall} staged={isStaged} inJob={IsInJob()}");

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
                            using var regKey = Services.SystemIntegrationService.UserHive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Reeled");
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

                    var stagedProc = StartOutsideJob(stagedExe, stagedArguments, stageDir);
                    Trace($"Staged uninstaller started pid={stagedProc?.Id} target={safeTargetDir}");
                    // Process.ExitCode is unavailable for a process this object didn't start (the WMI path),
                    // so wait and read the exit code through a native handle instead
                    int exitCode = stagedProc != null ? WaitForExitCode(stagedProc.Id) : -1;
                    Trace($"Staged uninstaller exited code={exitCode}");

                    if (exitCode == 0)
                    {
                        // A WMI-created uninstaller can't make lasting changes to the user's registry, so
                        // remove the Windows integration (incl. the Settings > Apps entry) from here, where
                        // HKCU is the real user hive. Idempotent when the staged process already did it.
                        new Services.SystemIntegrationService().RemoveWindowsIntegration();

                        try
                        {
                            if (!string.IsNullOrEmpty(safeTargetDir) && Directory.Exists(safeTargetDir))
                            {
                                var cleanupPsi = new ProcessStartInfo
                                {
                                    FileName = "cmd.exe",
                                    // Retry: this very process (Uninstall.exe in that folder) holds a lock until it exits
                                    Arguments = $"/c for /L %i in (1,1,30) do (rmdir /s /q \"{safeTargetDir}\" 2>nul & if exist \"{safeTargetDir}\" (timeout /t 1 /nobreak >nul) else (exit /b 0))",
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

            // Normal WinUI 3 startup. Load the self-contained Windows App Runtime first (what the SDK's
            // auto-initializer would have done before Main); the variable supports single-file extraction.
            // Mirrors the SDK's UndockedRegFreeWinRT auto-initializer (Foundation 2.3.x): MRT Core and reg-free
            // WinRT honor the base directory only when the PID stamp matches this process, which is how
            // single-file builds find their .pri and ms-appx:/// XAML resources.
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY_PID",
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WindowsAppRuntime_EnsureIsLoaded();
            Trace("Windows App Runtime loaded; starting WinUI");

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
