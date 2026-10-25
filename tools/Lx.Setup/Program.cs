using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lx.Setup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Any(a => string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase)))
            {
                RunSilent(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        private static void RunSilent(string[] args)
        {
            string target = DefaultTarget();
            foreach (string arg in args)
            {
                if (arg.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
                {
                    target = arg.Substring(3).Trim('"');
                }
            }

            try
            {
                Installer.Install(target, createShortcuts: false);
                File.WriteAllText(Path.Combine(target, "setup-silent.log"), "OK " + DateTime.Now);
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "setup-silent.log"), ex.ToString());
                }
                catch
                {
                }
            }
        }

        internal static string DefaultTarget()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Lx yulin carp");
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox _targetBox = new TextBox();
        private readonly CheckBox _desktopShortcut = new CheckBox();
        private readonly CheckBox _launchAfter = new CheckBox();
        private readonly Button _install = new Button();
        private readonly Button _browse = new Button();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _status = new Label();

        public SetupForm()
        {
            Text = "Lx yulin carp 1.4.0 安装";
            Width = 620;
            Height = 300;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "Lx yulin carp 1.4.0",
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                Left = 24,
                Top = 18,
                Width = 520,
                Height = 34,
            };

            var hint = new Label
            {
                Text = "安装到当前用户目录，不修改现有的 Lx 快捷方式。",
                Left = 26,
                Top = 56,
                Width = 560,
                Height = 22,
                ForeColor = Color.DimGray,
            };

            var pathLabel = new Label { Text = "安装目录：", Left = 26, Top = 96, Width = 80 };
            _targetBox.Left = 108;
            _targetBox.Top = 92;
            _targetBox.Width = 400;
            _targetBox.Text = Program.DefaultTarget();
            _browse.Text = "浏览…";
            _browse.Left = 516;
            _browse.Top = 90;
            _browse.Width = 70;
            _browse.Click += (_, __) =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.SelectedPath = _targetBox.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        _targetBox.Text = dialog.SelectedPath;
                    }
                }
            };

            _desktopShortcut.Text = "创建桌面快捷方式（新名称，不影响旧快捷方式）";
            _desktopShortcut.Left = 26;
            _desktopShortcut.Top = 132;
            _desktopShortcut.Width = 520;
            _desktopShortcut.Checked = true;

            _launchAfter.Text = "安装完成后启动";
            _launchAfter.Left = 26;
            _launchAfter.Top = 158;
            _launchAfter.Width = 260;
            _launchAfter.Checked = true;

            _install.Text = "安装";
            _install.Left = 410;
            _install.Top = 210;
            _install.Width = 80;
            _install.Height = 30;
            _install.Click += async (_, __) => await InstallAsync();

            var cancel = new Button { Text = "退出", Left = 500, Top = 210, Width = 80, Height = 30 };
            cancel.Click += (_, __) => Close();

            _progress.Left = 26;
            _progress.Top = 210;
            _progress.Width = 360;
            _progress.Height = 24;
            _progress.Style = ProgressBarStyle.Continuous;

            _status.Left = 26;
            _status.Top = 240;
            _status.Width = 550;
            _status.Height = 20;
            _status.ForeColor = Color.DimGray;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(pathLabel);
            Controls.Add(_targetBox);
            Controls.Add(_browse);
            Controls.Add(_desktopShortcut);
            Controls.Add(_launchAfter);
            Controls.Add(_progress);
            Controls.Add(_status);
            Controls.Add(_install);
            Controls.Add(cancel);
        }

        private async Task InstallAsync()
        {
            string target = _targetBox.Text.Trim();
            if (target.Length == 0)
            {
                MessageBox.Show(this, "安装目录不能为空", "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _install.Enabled = false;
            _browse.Enabled = false;
            _status.Text = "正在安装…";
            try
            {
                var progress = new Progress<int>(value => _progress.Value = value);
                await Task.Run(() => Installer.Install(target, _desktopShortcut.Checked, progress));
                _status.Text = "安装完成";
                MessageBox.Show(this, "安装完成。\n\n" + target, "Lx yulin carp", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (_launchAfter.Checked)
                {
                    Installer.Launch(target);
                }

                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "安装失败";
                MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _install.Enabled = true;
                _browse.Enabled = true;
            }
        }
    }

    internal static class Installer
    {
        public static void Install(string target, bool createShortcuts, IProgress<int> progress = null)
        {
            target = Path.GetFullPath(target);
            Directory.CreateDirectory(target);

            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (payload == null)
                {
                    throw new InvalidOperationException("安装包缺少 payload.zip");
                }

                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    int index = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        index++;
                        string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string destination = Path.GetFullPath(Path.Combine(target, relative));
                        if (!destination.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(destination, target, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("安装包包含非法路径：" + entry.FullName);
                        }

                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(destination);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, true);
                        progress?.Report((int)(index * 100.0 / archive.Entries.Count));
                    }
                }
            }

            if (createShortcuts)
            {
                string exe = Path.Combine(target, "Lx yulin carp.exe");
                CreateShortcut(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "Lx yulin carp.lnk"), exe, target);
                CreateShortcut(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Lx yulin carp.lnk"), exe, target);
            }
        }

        public static void Launch(string target)
        {
            string runner = Path.Combine(target, "run.cmd");
            string exe = Path.Combine(target, "Lx yulin carp.exe");
            if (File.Exists(runner))
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + runner + "\"")
                {
                    WorkingDirectory = target,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            else if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target });
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe, string workingDirectory)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.IconLocation = targetExe + ",0";
            shortcut.Description = "Lx yulin carp";
            shortcut.Save();
        }
    }
}
