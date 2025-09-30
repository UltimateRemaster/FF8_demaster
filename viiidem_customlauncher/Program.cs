using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace viiidem_customlauncher_min
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            // Base directory where this launcher is running
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Resolve FFVIII.exe (current dir, fallback: parent dir)
            string ff8Path = Path.Combine(baseDir, "FFVIII.exe");
            if (!File.Exists(ff8Path))
            {
                string parent = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar))?.FullName ?? baseDir;
                string alt = Path.Combine(parent, "FFVIII.exe");
                if (File.Exists(alt)) ff8Path = alt;
            }

            // Default behavior: NORMAL. Only hide when we receive --silent.
            bool silent = false;
            if (args != null)
            {
                foreach (var a in args)
                {
                    if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase))
                    {
                        silent = true;
                        break;
                    }
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = ff8Path,
                WorkingDirectory = Path.GetDirectoryName(ff8Path) ?? baseDir
            };

            if (silent)
            {
                // Hide this launcher's own console window and start the game
                psi.UseShellExecute = false;   // required for CreateNoWindow
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
            }
            else
            {
                // Normal mode: let Windows manage windows normally
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Normal;
            }

            using (var proc = Process.Start(psi))
            {
                // In silent mode, actively hide GUI windows of the child process by PID.
                // This mirrors the working approach from your original Program.cs
                if (silent && proc != null)
                {
                    var until = DateTime.UtcNow.AddSeconds(15); // short time window to catch initial/splash windows
                    while (!proc.HasExited && DateTime.UtcNow < until)
                    {
                        // Best-effort: hide any visible window that belongs to the game's PID
                        HideWindowsByPid(proc.Id, "FFVIII.exe");
                        Thread.Sleep(250);
                    }
                }
            }
        }

        // ==== Window hiding  ====

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const int SW_HIDE = 0;

        // Hide all visible top-level windows that belong to the given PID.
        // titleNeedle is optional; when provided, we also check title contains the needle.
        private static void HideWindowsByPid(int pid, string titleNeedle = null)
        {
            EnumWindows((hWnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd))
                        return true;

                    GetWindowThreadProcessId(hWnd, out uint wPid);
                    if (wPid != (uint)pid)
                        return true;

                    int len = GetWindowTextLength(hWnd);
                    var sb = len > 0 ? new StringBuilder(len + 1) : new StringBuilder(1);
                    if (len > 0) GetWindowText(hWnd, sb, sb.Capacity);

                    string title = sb.ToString();

                    if (string.IsNullOrEmpty(titleNeedle) ||
                        (!string.IsNullOrEmpty(title) &&
                         title.IndexOf(titleNeedle, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        ShowWindow(hWnd, SW_HIDE);
                    }
                }
                catch
                {
                    // swallow to keep the loop minimal & resilient
                }
                return true; // continue enumeration
            }, IntPtr.Zero);
        }
    }
}
