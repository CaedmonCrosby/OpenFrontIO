using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private const int ClientPort = 9000;
    private const string AppUrl = "http://127.0.0.1:9000";
    private const string MutexName = "Local\\OpenFront.Caedmon.App";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool created;
        var mutex = new Mutex(true, MutexName, out created);
        if (!created)
        {
            OpenGameWindow();
            return;
        }

        var root = FindRepoRoot();
        if (root == null)
        {
            MessageBox.Show("OpenFront files were not found.", "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var nodeDir = FindNodeDir();
        if (nodeDir == null)
        {
            MessageBox.Show("Node.js was not found. Install it from https://nodejs.org/", "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var splash = MakeSplash("Starting OpenFront…");
        splash.Show();
        Application.DoEvents();

        Process vite = null;
        Process server = null;
        try
        {
            FreeListenPorts(new[] { 9000, 3000, 3001, 3002 });
            Thread.Sleep(400);

            if (!Directory.Exists(Path.Combine(root, "node_modules")))
            {
                SetSplash(splash, "Installing (first launch only)…");
                var inst = StartNode(nodeDir, "\"" + Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js") + "\" run inst", root, null);
                inst.WaitForExit();
                if (inst.ExitCode != 0)
                {
                    splash.Close();
                    MessageBox.Show("Could not install OpenFront. Check your internet connection and try again.", "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            SetSplash(splash, "Starting…");
            var nodeExe = Path.Combine(nodeDir, "node.exe");
            var viteJs = Path.Combine(root, "node_modules", "vite", "bin", "vite.js");
            var tsxJs = Path.Combine(root, "node_modules", "tsx", "dist", "cli.mjs");

            vite = StartNode(
                nodeDir,
                "\"" + viteJs + "\" --port 9000 --strictPort --host 127.0.0.1",
                root,
                DevEnv(true));
            server = StartNode(
                nodeDir,
                "\"" + tsxJs + "\" src/server/Server.ts",
                root,
                DevEnv(false));

            if (!WaitForPort("127.0.0.1", ClientPort, TimeSpan.FromSeconds(45), splash, vite))
            {
                KillTree(vite);
                KillTree(server);
                splash.Close();
                MessageBox.Show("OpenFront could not start. Close other copies and try again.", "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetSplash(splash, "Opening…");
            OpenGameWindow();
            splash.Close();

            var ctl = MakeControlWindow();
            Application.Run(ctl);
        }
        catch (Exception)
        {
            try { splash.Close(); } catch { }
            MessageBox.Show("OpenFront could not start.", "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            KillTree(vite);
            KillTree(server);
            try { mutex.ReleaseMutex(); } catch { }
            mutex.Close();
        }
    }

    private static Dictionary<string, string> DevEnv(bool forVite)
    {
        var env = new Dictionary<string, string>();
        env["SKIP_BROWSER_OPEN"] = "true";
        env["GAME_ENV"] = "dev";
        env["DOMAIN"] = "localhost";
        env["GIT_COMMIT"] = "DEV";
        env["NUM_WORKERS"] = "1";
        env["TURNSTILE_SITE_KEY"] = "1x00000000000000000000AA";
        env["API_KEY"] = "WARNING_DEV_API_KEY_DO_NOT_USE_IN_PRODUCTION";
        env["ADMIN_BOT_API_KEY"] = "WARNING_DEV_ADMIN_BOT_KEY_DO_NOT_USE_IN_PRODUCTION";
        if (forVite) env["VITE_HOST"] = "";
        return env;
    }

    private static void OpenGameWindow()
    {
        var edge = FindEdge();
        if (edge == null)
        {
            MessageBox.Show("Microsoft Edge is required for OpenFront.", "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var root = FindRepoRoot() ?? AppDomain.CurrentDomain.BaseDirectory;
        var profile = Path.Combine(root, ".openfront-app-profile");
        Directory.CreateDirectory(profile);
        Process.Start(new ProcessStartInfo
        {
            FileName = edge,
            Arguments = "--app=" + AppUrl
                + " --user-data-dir=\"" + profile + "\""
                + " --window-size=1440,900"
                + " --disable-features=Translate,MediaRouter",
            UseShellExecute = false,
        });
    }

    private static Form MakeSplash(string text)
    {
        var form = new Form
        {
            Text = "OpenFront",
            Width = 360,
            Height = 130,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ControlBox = false,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(11, 15, 20),
            ForeColor = Color.White,
        };
        form.Controls.Add(new Label
        {
            Name = "status",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12f),
            ForeColor = Color.White,
            Text = text,
        });
        return form;
    }

    private static Form MakeControlWindow()
    {
        var form = new Form
        {
            Text = "OpenFront",
            Width = 380,
            Height = 130,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            BackColor = Color.FromArgb(11, 15, 20),
            ForeColor = Color.White,
        };
        form.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11f),
            ForeColor = Color.White,
            Text = "OpenFront is running.\nClose this window to quit.",
        });
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

    private static Process StartNode(string nodeDir, string args, string workDir, Dictionary<string, string> extraEnv)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(nodeDir, "node.exe"),
            Arguments = args,
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
        if (extraEnv != null)
        {
            foreach (var kv in extraEnv) psi.EnvironmentVariables[kv.Key] = kv.Value;
        }
        var p = new Process { StartInfo = psi };
        p.OutputDataReceived += delegate { };
        p.ErrorDataReceived += delegate { };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static bool WaitForPort(string host, int port, TimeSpan timeout, Form splash, Process child)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (child != null && child.HasExited) return false;
            if (PortIsOpen(host, port)) return true;
            Application.DoEvents();
            Thread.Sleep(250);
        }
        return false;
    }

    private static bool PortIsOpen(string host, int port)
    {
        try
        {
            using (var client = new TcpClient())
            {
                var ar = client.BeginConnect(host, port, null, null);
                return ar.AsyncWaitHandle.WaitOne(300) && client.Connected;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void FreeListenPorts(int[] ports)
    {
        var pids = new Dictionary<int, bool>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            if (p == null) return;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            using (var reader = new StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.IndexOf("LISTENING") < 0) continue;
                    for (var i = 0; i < ports.Length; i++)
                    {
                        if (line.IndexOf(":" + ports[i] + " ") < 0 && line.IndexOf(":" + ports[i] + "\t") < 0) continue;
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0) continue;
                        int pid;
                        if (int.TryParse(parts[parts.Length - 1], out pid) && pid > 0)
                        {
                            pids[pid] = true;
                        }
                    }
                }
            }
        }
        catch { }

        var self = Process.GetCurrentProcess().Id;
        foreach (var pid in pids.Keys)
        {
            if (pid == self) continue;
            try
            {
                var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/F /PID " + pid + " /T",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (killer != null) killer.WaitForExit(3000);
            }
            catch { }
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
