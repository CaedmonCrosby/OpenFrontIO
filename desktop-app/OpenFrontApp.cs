using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private const int Port = 9000;
    private const string AppUrl = "http://127.0.0.1:9000";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var root = FindRepoRoot();
        if (root == null)
        {
            MessageBox.Show(
                "Could not find the OpenFront folder (package.json).",
                "OpenFront",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var splash = MakeSplash("Starting OpenFront…");
        splash.Show();
        Application.DoEvents();

        try
        {
            var npm = FindNpm();
            if (npm == null)
            {
                splash.Close();
                MessageBox.Show(
                    "Node.js / npm was not found. Install Node.js from https://nodejs.org/ then try again.",
                    "OpenFront",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (!Directory.Exists(Path.Combine(root, "node_modules")))
            {
                SetSplash(splash, "Installing game files (first launch only)…");
                var inst = StartHidden(npm, "run inst", root);
                inst.WaitForExit();
                if (inst.ExitCode != 0)
                {
                    splash.Close();
                    MessageBox.Show(
                        "Installing dependencies failed. Open a terminal in the OpenFront folder and run: npm run inst",
                        "OpenFront",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }

            SetSplash(splash, "Launching game server…");
            var server = StartHidden(npm, "run dev", root, "SKIP_BROWSER_OPEN=true");

            if (!WaitForPort(Port, TimeSpan.FromMinutes(3), splash))
            {
                KillTree(server);
                splash.Close();
                MessageBox.Show(
                    "The game server did not start. Check that port 9000 is free.",
                    "OpenFront",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var edge = FindEdge();
            if (edge == null)
            {
                KillTree(server);
                splash.Close();
                MessageBox.Show(
                    "Microsoft Edge was not found. It is required for the OpenFront window.",
                    "OpenFront",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var profile = Path.Combine(root, ".openfront-app-profile");
            Directory.CreateDirectory(profile);
            SetSplash(splash, "Opening OpenFront…");

            var window = Process.Start(new ProcessStartInfo
            {
                FileName = edge,
                Arguments = "--app=" + AppUrl
                    + " --user-data-dir=\"" + profile + "\""
                    + " --window-size=1440,900"
                    + " --disable-features=Translate,MediaRouter",
                UseShellExecute = false,
            });

            splash.Close();

            if (window == null)
            {
                KillTree(server);
                return;
            }

            window.WaitForExit();
            KillTree(server);
        }
        catch (Exception ex)
        {
            try { splash.Close(); } catch { }
            MessageBox.Show(ex.Message, "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Form MakeSplash(string text)
    {
        var form = new Form
        {
            Text = "OpenFront",
            Width = 460,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ControlBox = false,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(11, 15, 20),
            ForeColor = Color.White,
        };
        var label = new Label
        {
            Name = "status",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12f),
            ForeColor = Color.White,
            Text = text,
        };
        form.Controls.Add(label);
        return form;
    }

    private static void SetSplash(Form splash, string text)
    {
        var label = splash.Controls["status"] as Label;
        if (label != null) label.Text = text;
        Application.DoEvents();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "package.json"))
                && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string FindNpm()
    {
        var node = FindOnPath("node.exe") ?? FindOnPath("node");
        if (node != null)
        {
            var dir = Path.GetDirectoryName(node);
            var npm = Path.Combine(dir ?? "", "npm.cmd");
            if (File.Exists(npm)) return npm;
        }
        foreach (var candidate in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npm.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npm.cmd"),
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return FindOnPath("npm.cmd");
    }

    private static string FindEdge()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe"),
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return FindOnPath("msedge.exe");
    }

    private static string FindOnPath(string name)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = name,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            if (p == null) return null;
            var line = p.StandardOutput.ReadLine();
            p.WaitForExit();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static Process StartHidden(string fileName, string arguments, string workDir, string extraEnv = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + fileName + "\" " + arguments,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.EnvironmentVariables["SKIP_BROWSER_OPEN"] = "true";
        psi.EnvironmentVariables["GAME_ENV"] = "dev";
        if (extraEnv != null)
        {
            var parts = extraEnv.Split(new[] { '=' }, 2);
            if (parts.Length == 2) psi.EnvironmentVariables[parts[0]] = parts[1];
        }
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, __) => { };
        p.ErrorDataReceived += (_, __) => { };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static bool WaitForPort(int port, TimeSpan timeout, Form splash)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect("127.0.0.1", port, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(400) && client.Connected)
                    {
                        return true;
                    }
                }
            }
            catch { }
            Application.DoEvents();
            Thread.Sleep(400);
        }
        return false;
    }

    private static void KillTree(Process process)
    {
        if (process == null) return;
        try
        {
            var killer = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/PID " + process.Id + " /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (killer != null) killer.WaitForExit(5000);
        }
        catch { }
    }
}
