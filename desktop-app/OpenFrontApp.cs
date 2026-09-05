using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

internal class MainForm : Form
{
    private readonly Label status;
    private readonly WebView2 web;
    private Process vite;
    private Process server;

    public MainForm()
    {
        Text = "OpenFront";
        Width = 1440;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 15, 20);
        MinimumSize = new Size(800, 600);

        status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14f),
            ForeColor = Color.White,
            Text = "Starting OpenFront…",
        };
        web = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        Controls.Add(web);
        Controls.Add(status);

        Shown += OnShown;
        FormClosed += OnFormClosed;
    }

    private void OnShown(object sender, EventArgs e)
    {
        try
        {
            if (!HttpOk())
            {
                var root = FindRepoRoot();
                var nodeDir = FindNodeDir();
                if (root == null || nodeDir == null)
                {
                    status.Text = "Could not find Node.js or the OpenFront files.";
                    return;
                }
                var nodeExe = Path.Combine(nodeDir, "node.exe");
                vite = StartNode(nodeExe, nodeDir, root,
                    "\"" + Path.Combine(root, "node_modules", "vite", "bin", "vite.js") + "\" --port 9000 --strictPort --host 127.0.0.1",
                    true);
                server = StartNode(nodeExe, nodeDir, root,
                    "\"" + Path.Combine(root, "node_modules", "tsx", "dist", "cli.mjs") + "\" src/server/Server.ts",
                    false);
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < deadline && !HttpOk())
                {
                    Application.DoEvents();
                    Thread.Sleep(300);
                }
                if (!HttpOk())
                {
                    status.Text = "OpenFront could not start.";
                    return;
                }
            }

            status.Text = "Opening…";
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenFront",
                "webview2");
            Directory.CreateDirectory(dataDir);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", dataDir);

            web.CoreWebView2InitializationCompleted += OnWebViewReady;
            web.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            status.Text = "OpenFront could not start.";
            Debug.WriteLine(ex.ToString());
        }
    }

    private void OnWebViewReady(object sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            status.Text = "The game window could not be created.";
            return;
        }
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        web.CoreWebView2.Navigate("http://127.0.0.1:9000/");
        web.Visible = true;
        status.Visible = false;
    }

    private void OnFormClosed(object sender, FormClosedEventArgs e)
    {
        KillTree(vite);
        KillTree(server);
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
