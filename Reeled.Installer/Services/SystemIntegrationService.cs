using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Reeled.Installer.Services;

public class SystemIntegrationService
{
    /// <summary>
    /// The signed-in user's registry hive. Registry.CurrentUser is not safe here: an uninstaller started
    /// from Windows Settings is relaunched through WMI (to escape Settings' job object), and processes WMI
    /// creates run without the user profile loaded, so HKEY_CURRENT_USER maps to HKU\.DEFAULT. The user's
    /// own hive is always loaded under HKEY_USERS\&lt;SID&gt; while they are logged on.
    /// </summary>
    internal static RegistryKey UserHive => _userHive.Value;

    private static readonly Lazy<RegistryKey> _userHive = new(OpenUserHive);

    private static RegistryKey OpenUserHive()
    {
        {
            try
            {
                string? sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
                if (!string.IsNullOrEmpty(sid))
                {
                    var hive = Registry.Users.OpenSubKey(sid, writable: true);
                    Program.Trace($"User hive {sid}: {(hive != null ? "opened" : "not loaded")}");
                    if (hive != null) return hive;
                }
            }
            catch (Exception ex)
            {
                Program.Trace($"Opening user hive failed: {ex.Message}");
            }
            return Registry.CurrentUser;
        }
    }

    /// <summary>Product version from the assembly, e.g. "1.1.0".</summary>
    internal static string AppVersion
    {
        get
        {
            var v = typeof(SystemIntegrationService).Assembly.GetName().Version;
            return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    private static readonly string[] VideoExtensions = new[]
    {
        ".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".flv", ".m4v", ".ts"
    };

    public void CreateShortcuts(string targetExePath, bool desktopShortcut, bool startMenuShortcut)
    {
        string? targetDir = Path.GetDirectoryName(targetExePath);
        if (string.IsNullOrEmpty(targetDir) || !File.Exists(targetExePath))
            return;

        if (desktopShortcut)
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string desktopLnk = Path.Combine(desktopDir, "Reeled.lnk");
            CreateLnkShortcut(desktopLnk, targetExePath, targetDir, "Reeled Gaming Clips Library");
        }

        if (startMenuShortcut)
        {
            string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            Directory.CreateDirectory(startMenuDir);
            string startMenuLnk = Path.Combine(startMenuDir, "Reeled.lnk");
            CreateLnkShortcut(startMenuLnk, targetExePath, targetDir, "Reeled Gaming Clips Library");
        }
    }

    private void CreateLnkShortcut(string shortcutPath, string targetPath, string workingDirectory, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = workingDirectory;
                    shortcut.Description = description;
                    shortcut.IconLocation = targetPath + ",0";
                    shortcut.Save();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Shortcut Error] {ex.Message}");
        }
    }

    public void RegisterWindowsIntegration(string installDir, bool registerAsVideoPlayer)
    {
        string exePath = Path.Combine(installDir, "Reeled.exe");
        if (!File.Exists(exePath)) return;

        try
        {
            // 1. Register Application Under HKCU\Software\RegisteredApplications
            using (var regApps = UserHive.CreateSubKey(@"Software\RegisteredApplications"))
            {
                regApps.SetValue("Reeled", @"Software\Reeled\Capabilities");
            }

            // 2. Capabilities Definition
            using (var capKey = UserHive.CreateSubKey(@"Software\Reeled\Capabilities"))
            {
                capKey.SetValue("ApplicationName", "Reeled");
                capKey.SetValue("ApplicationDescription", "The modern high-performance gaming clips library and player.");
                capKey.SetValue("ApplicationIcon", $"{exePath},0");

                if (registerAsVideoPlayer)
                {
                    using var fileAssoc = capKey.CreateSubKey("FileAssociations");
                    foreach (var ext in VideoExtensions)
                    {
                        fileAssoc.SetValue(ext, "Reeled.Video");
                    }
                }
            }

            // 3. ProgID Registration: Reeled.Video
            using (var progId = UserHive.CreateSubKey(@"Software\Classes\Reeled.Video"))
            {
                progId.SetValue("", "Video clip");
                progId.SetValue("FriendlyTypeName", "Reeled Video Clip");

                using var defIcon = progId.CreateSubKey("DefaultIcon");
                defIcon.SetValue("", $"{exePath},0");

                using var shellOpen = progId.CreateSubKey(@"shell\open\command");
                shellOpen.SetValue("", $"\"{exePath}\" \"%1\"");
            }

            // 4. Register in Applications for Explorer and "Open with"
            using (var appKey = UserHive.CreateSubKey(@"Software\Classes\Applications\Reeled.exe"))
            {
                appKey.SetValue("FriendlyAppName", "Reeled");

                using var defIcon = appKey.CreateSubKey("DefaultIcon");
                defIcon.SetValue("", $"{exePath},0");

                using var cmd = appKey.CreateSubKey(@"shell\open\command");
                cmd.SetValue("", $"\"{exePath}\" \"%1\"");

                using var supportedTypes = appKey.CreateSubKey("SupportedTypes");
                foreach (var ext in VideoExtensions)
                {
                    supportedTypes.SetValue(ext, "");
                }
            }

            // 5. Register in SystemFileAssociations for Explorer "Open with" context menu on video files
            using (var sfaVideo = UserHive.CreateSubKey(@"Software\Classes\SystemFileAssociations\video\OpenWithList\Reeled.exe"))
            {
                // Registering the key ensures Reeled appears in Explorer context menu -> "Open with"
            }

            // 6. Add to each extension's OpenWithProgids & OpenWithList
            if (registerAsVideoPlayer)
            {
                foreach (var ext in VideoExtensions)
                {
                    try
                    {
                        using var extKey = UserHive.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                        extKey.SetValue("Reeled.Video", "");

                        using var openWithList = UserHive.CreateSubKey($@"Software\Classes\{ext}\OpenWithList\Reeled.exe");
                    }
                    catch { }
                }
            }

            // Notify Windows Shell of association changes
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Registry Integration Error] {ex.Message}");
        }
    }

    public void RegisterUninstaller(string installDir, long estimatedSizeBytes)
    {
        string exePath = Path.Combine(installDir, "Reeled.exe");
        string setupExe = Path.Combine(installDir, "ReeledSetup.exe");
        string uninstallExe = Path.Combine(installDir, "Uninstall.exe");
        string cmdTarget = File.Exists(uninstallExe) ? uninstallExe : (File.Exists(setupExe) ? setupExe : uninstallExe);

        string uninstTarget = Path.GetFileNameWithoutExtension(cmdTarget).Equals("Uninstall", StringComparison.OrdinalIgnoreCase)
            ? $"\"{cmdTarget}\""
            : $"\"{cmdTarget}\" /uninstall";

        try
        {
            using var uninstKey = UserHive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Reeled");
            uninstKey.SetValue("DisplayName", "Reeled");
            uninstKey.SetValue("DisplayVersion", AppVersion);
            uninstKey.SetValue("Publisher", "k25jura");
            uninstKey.SetValue("DisplayIcon", $"{exePath},0");
            uninstKey.SetValue("InstallLocation", installDir);
            uninstKey.SetValue("UninstallString", uninstTarget);
            uninstKey.SetValue("QuietUninstallString", $"\"{cmdTarget}\" /silent");
            uninstKey.SetValue("EstimatedSize", (int)(estimatedSizeBytes / 1024));
            uninstKey.SetValue("URLInfoAbout", "https://github.com/k25jura/Reeled");
            uninstKey.SetValue("HelpLink", "https://github.com/k25jura/Reeled/issues");
            uninstKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
            uninstKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Uninstaller Registration Error] {ex.Message}");
        }
    }

    public void RemoveShortcuts()
    {
        try
        {
            string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Reeled.lnk");
            if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

            string startMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Reeled.lnk");
            if (File.Exists(startMenuLnk)) File.Delete(startMenuLnk);
        }
        catch { }
    }

    public void RemoveWindowsIntegration()
    {
        // Each step is independent: previously the first failure skipped everything after it, including
        // the Add/Remove Programs entry, so Settings kept listing an app that was already gone.
        void Step(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { Program.Trace($"RemoveWindowsIntegration {name} failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        Step("RegisteredApplications", () =>
        {
            using var regApps = UserHive.OpenSubKey(@"Software\RegisteredApplications", writable: true);
            regApps?.DeleteValue("Reeled", throwOnMissingValue: false);
        });
        Step("Capabilities", () => UserHive.DeleteSubKeyTree(@"Software\Reeled", throwOnMissingSubKey: false));
        Step("ProgId", () => UserHive.DeleteSubKeyTree(@"Software\Classes\Reeled.Video", throwOnMissingSubKey: false));
        Step("Applications", () => UserHive.DeleteSubKeyTree(@"Software\Classes\Applications\Reeled.exe", throwOnMissingSubKey: false));
        Step("SystemFileAssociations", () => UserHive.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\video\OpenWithList\Reeled.exe", throwOnMissingSubKey: false));

        foreach (var ext in VideoExtensions)
        {
            Step($"OpenWith {ext}", () =>
            {
                using (var extKey = UserHive.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true))
                {
                    extKey?.DeleteValue("Reeled.Video", throwOnMissingValue: false);
                }
                UserHive.DeleteSubKeyTree($@"Software\Classes\{ext}\OpenWithList\Reeled.exe", throwOnMissingSubKey: false);
            });
        }

        Step("UninstallKey", () => UserHive.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Reeled", throwOnMissingSubKey: false));
        Step("Notify", () => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero));

        using var remaining = UserHive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Reeled");
        Program.Trace($"RemoveWindowsIntegration done; uninstall key still present={remaining != null}");
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
