using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WasdshotSetup
{
    static class Program
    {
        const string AppName = "wasdshot";
        const string Version = "1.0.0";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            string installDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
            bool uninstall = Array.Exists(args, value => value.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            try
            {
                if (uninstall) Uninstall(installDirectory);
                else Install(installDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось " + (uninstall ? "удалить" : "установить") + " wasdshot:\n\n" + ex.Message, "wasdshot setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void Install(string installDirectory)
        {
            if (MessageBox.Show("Установить wasdshot v1?", "wasdshot setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            StopRunningApp();
            Directory.CreateDirectory(installDirectory);
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("wasdshot.payload.zip"))
            {
                if (resource == null) throw new InvalidOperationException("В установщике отсутствуют файлы приложения.");
                using (ZipArchive archive = new ZipArchive(resource, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string destination = Path.GetFullPath(Path.Combine(installDirectory, entry.FullName));
                        string root = Path.GetFullPath(installDirectory) + Path.DirectorySeparatorChar;
                        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Некорректный путь в установщике.");
                        if (String.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, true);
                    }
                }
            }

            string setupCopy = Path.Combine(installDirectory, "uninstall.exe");
            File.Copy(Application.ExecutablePath, setupCopy, true);
            string appExe = Path.Combine(installDirectory, "wasdshot.exe");
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "wasdshot.lnk"), appExe);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\wasdshot"))
            {
                key.SetValue("DisplayName", "wasdshot");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "wasdshot");
                key.SetValue("InstallLocation", installDirectory);
                key.SetValue("DisplayIcon", appExe);
                key.SetValue("UninstallString", "\"" + setupCopy + "\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            Process.Start(appExe);
            MessageBox.Show("wasdshot v1 установлен и запущен.\n\nWin + Shift + S — сделать скриншот.", "wasdshot setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static void Uninstall(string installDirectory)
        {
            if (MessageBox.Show("Удалить wasdshot?", "wasdshot setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            StopRunningApp();
            string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "wasdshot.lnk");
            if (File.Exists(shortcut)) File.Delete(shortcut);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\wasdshot", false);
            string command = "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + installDirectory + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            MessageBox.Show("wasdshot удалён.", "wasdshot setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static void StopRunningApp()
        {
            foreach (Process process in Process.GetProcessesByName("wasdshot"))
            {
                try { process.Kill(); process.WaitForExit(3000); }
                catch { }
                finally { process.Dispose(); }
            }
        }

        static void CreateShortcut(string shortcutPath, string targetPath)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            object shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(targetPath) });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "wasdshot screenshot tool" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            MarshalRelease(shortcut);
            MarshalRelease(shell);
        }

        static void MarshalRelease(object value)
        {
            if (value != null && System.Runtime.InteropServices.Marshal.IsComObject(value)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
        }
    }
}
