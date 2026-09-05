using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private const string AppUrl = "http://127.0.0.1:9000/";
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

        var splash = MakeSplash("Starting OpenFront…");
        splash.Show();
        Application.DoEvents();

        Process vite = null;
        Process server = null;
        try
        {
            if (!HttpOk())
            {
                var root = FindRepoRoot();
                var nodeDir = FindNodeDir();
                if (root == null || nodeDir == null)
                {
                    splash.Close();
                    MessageBox.Show("OpenFront could not find Node.js or its files.", "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                SetSplash(splash, "Starting…");
                var nodeExe = Path.Combine(nodeDir, "node.exe");
                vite = StartNode(nodeExe, nodeDir, root,
                    "\"" + Path.Combine(root, "node_modules", "vite", "bin", "vite.js") + "\" --port 9000 --strictPort --host 127.0.0.1",
                    true);
                server = StartNode(nodeExe, nodeDir, root,
                    "\"" + Path.Combine(root, "node_modules", "tsx", "dist", "cli.mjs") + "\" src/server/Server.ts",
                    false);

                if (!WaitForHttp(TimeSpan.FromSeconds(60), splash))
                {
                    splash.Close();
                    MessageBox.Show("OpenFront could not start. Try again in a moment.", "OpenFront", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    KillTree(vite);
                    KillTree(server);
                    return;
                }
            }

            SetSplash(splash, "Opening…");
            OpenGameWindow();
            splash.Close();

            Application.Run(MakeQuitWindow());
        }
        catch
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

    private static Process StartNode(string nodeExe, string nodeDir, string root, string args, bool vite)
    {
        var psi = new ProcessStartInfo
        {
            FileName = nodeExe,
            Arguments = args,
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.EnvironmentVariables["PATH"] = nodeDir + ";" + Path.Combine(root, "node_modules", ".bin") + ";" + path;
        psi.EnvironmentVariables["SKIP_BROWSER_OPEN"] = "true";
        psi.EnvironmentVariables["GAME_ENV"] = "dev";
        psi.EnvironmentVariables["NUM_WORKERS"] = "1";
        psi.EnvironmentVariables["DOMAIN"] = "localhost";
        psi.EnvironmentVariables["GIT_COMMIT"] = "DEV";
        psi.EnvironmentVariables["TURNSTILE_SITE_KEY"] = "1x00000000000000000000AA";
        psi.EnvironmentVariables["API_KEY"] = "WARNING_DEV_API_KEY_DO_NOT_USE_IN_PRODUCTION";
        psi.EnvironmentVariables["ADMIN_BOT_API_KEY"] = "WARNING_DEV_ADMIN_BOT_KEY_DO_NOT_USE_IN_PRODUCTION";
        return Process.Start(psi);
    }

    private static void OpenGameWindow()
    {
        var edge = FindEdge();
        var root = FindRepoRoot() ?? AppDomain.CurrentDomain.BaseDirectory;
        var profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenFront",
            "edge-profile");
        Directory.CreateDirectory(profile);
        if (edge != null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = edge,
                Arguments = "--app=http://127.0.0.1:9000"
                    + " --user-data-dir=\"" + profile + "\""
                    + " --window-size=1440,900"
                    + " --proxy-server=direct://"
                    + " --proxy-bypass-list=127.0.0.1;localhost;*"
                    + " --disable-features=Translate,MediaRouter",
                UseShellExecute = false,
            });
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = AppUrl, UseShellExecute = true });
    }

    private static bool WaitForHttp(TimeSpan timeout, Form splash)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (HttpOk()) return true;
            Application.DoEvents();
            Thread.Sleep(300);
        }
        return false;
    }

    private static bool HttpOk()
    {
        try
        {
            using (var client = new TcpClient())
            {
                var ar = client.BeginConnect("127.0.0.1", 9000, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(800) || !client.Connected) return false;
                client.EndConnect(ar);
                using (var stream = client.GetStream())
                {
                    stream.ReadTimeout = 1500;
                    stream.WriteTimeout = 1500;
                    var bytes = Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\nHost: 127.0.0.1:9000\r\nConnection: close\r\n\r\n");
                    stream.Write(bytes, 0, bytes.Length);
                    var buf = new byte[24];
                    var n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) return false;
                    return Encoding.ASCII.GetString(buf, 0, n).IndexOf("HTTP/1.") == 0;
                }
            }
        }
        catch
        {
            return false;
        }
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

    private static Form MakeQuitWindow()
    {
        var form = new Form
        {
            Text = "OpenFront",
            Width = 400,
            Height = 140,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            BackColor = Color.FromArgb(11, 15, 20),
        };
        form.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11f),
            ForeColor = Color.White,
            Text = "OpenFront is running.\nKeep this window open while you play.\nClose it to quit.",
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
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string FindNodeDir()
    {
        foreach (var dir in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs"),
        })
        {
            if (File.Exists(Path.Combine(dir, "node.exe"))) return dir;
        }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "node.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            var line = p.StandardOutput.ReadLine();
            p.WaitForExit();
            if (!string.IsNullOrEmpty(line) && File.Exists(line.Trim()))
                return Path.GetDirectoryName(line.Trim());
        }
        catch { }
        return null;
    }

    private static string FindEdge()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
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
            if (killer != null) killer.WaitForExit(4000);
        }
        catch { }
    }
}
