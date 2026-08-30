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
    private const string AppUrl = "http://localhost:9000";

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
            var nodeDir = FindNodeDir();
            if (nodeDir == null)
            {
                splash.Close();
                MessageBox.Show(
                    "Node.js was not found. Install it from https://nodejs.org/ then try again.",
                    "OpenFront",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var logPath = Path.Combine(root, "desktop-app", "launch.log");
            Process server = null;

            if (PortIsOpen(Port))
            {
                SetSplash(splash, "Game server already running…");
            }
            else
            {
                if (!Directory.Exists(Path.Combine(root, "node_modules")))
                {
                    SetSplash(splash, "Installing game files (first launch only)…");
                    var inst = StartNpm(nodeDir, "run inst", root, logPath);
                    inst.WaitForExit();
                    if (inst.ExitCode != 0)
                    {
                        splash.Close();
                        MessageBox.Show(
                            "Installing dependencies failed. See desktop-app\\launch.log",
                            "OpenFront",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }

                SetSplash(splash, "Launching game server (usually ~5 seconds)…");
                server = StartNpm(nodeDir, "run dev", root, logPath);

                if (!WaitForPort(Port, TimeSpan.FromSeconds(90), splash, server))
                {
                    KillTree(server);
                    splash.Close();
                    MessageBox.Show(
                        "The game server did not start.\n\nLast log:\n" + TailLog(logPath, 20),
                        "OpenFront",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
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

            Process.Start(new ProcessStartInfo
            {
                FileName = edge,
                Arguments = "--app=" + AppUrl
                    + " --user-data-dir=\"" + profile + "\""
                    + " --window-size=1440,900"
                    + " --disable-features=Translate,MediaRouter",
                UseShellExecute = false,
            });

            splash.Close();

            var ctl = new Form
            {
                Text = "OpenFront",
                Width = 420,
                Height = 140,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                BackColor = Color.FromArgb(11, 15, 20),
                ForeColor = Color.White,
            };
            ctl.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11f),
                ForeColor = Color.White,
                Text = "OpenFront is running.\nClose this window to stop the game.",
            });
            Application.Run(ctl);
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

    private static string FindNodeDir()
    {
        var node = FindOnPath("node.exe") ?? FindOnPath("node");
        if (!string.IsNullOrEmpty(node) && File.Exists(node))
        {
            return Path.GetDirectoryName(node);
        }
        foreach (var dir in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs"),
        })
        {
            if (File.Exists(Path.Combine(dir, "node.exe"))) return dir;
        }
        return null;
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

    private static Process StartNpm(string nodeDir, string npmArgs, string workDir, string logPath)
    {
        var nodeExe = Path.Combine(nodeDir, "node.exe");
        var npmCli = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");
        var psi = new ProcessStartInfo
        {
            FileName = nodeExe,
            Arguments = "\"" + npmCli + "\" " + npmArgs,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var path = psi.EnvironmentVariables["PATH"] ?? "";
        psi.EnvironmentVariables["PATH"] = nodeDir + ";"
            + Path.Combine(workDir, "node_modules", ".bin") + ";"
            + path;
        psi.EnvironmentVariables["SKIP_BROWSER_OPEN"] = "true";
        psi.EnvironmentVariables["GAME_ENV"] = "dev";

        try { File.WriteAllText(logPath, "Starting: " + nodeExe + " " + psi.Arguments + "\r\n"); }
        catch { }

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        DataReceivedEventHandler write = delegate(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            try { File.AppendAllText(logPath, e.Data + "\r\n"); } catch { }
        };
        p.OutputDataReceived += write;
        p.ErrorDataReceived += write;
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static bool PortIsOpen(int port)
    {
        // Vite binds "localhost", which on Windows is often IPv6 (::1) only.
        foreach (var host in new[] { "localhost", "127.0.0.1", "::1" })
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(host, port, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(400) && client.Connected)
                    {
                        return true;
                    }
                }
            }
            catch { }
        }
        return false;
    }

    private static bool WaitForPort(int port, TimeSpan timeout, Form splash, Process server)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (server != null && server.HasExited) return false;
            if (PortIsOpen(port)) return true;
            Application.DoEvents();
            Thread.Sleep(300);
        }
        return false;
    }

    private static string TailLog(string logPath, int lines)
    {
        try
        {
            if (!File.Exists(logPath)) return "(no log)";
            var all = File.ReadAllLines(logPath);
            var start = Math.Max(0, all.Length - lines);
            var sb = new System.Text.StringBuilder();
            for (var i = start; i < all.Length; i++) sb.AppendLine(all[i]);
            var text = sb.ToString().Trim();
            if (text.Length > 1200) text = text.Substring(text.Length - 1200);
            return text;
        }
        catch
        {
            return "(could not read log)";
        }
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
