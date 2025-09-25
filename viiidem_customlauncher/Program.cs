using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace viiidem_customlauncher
{
    static class Program
    {
        // --- SUPRESSÃO DO ARQUIVO eden_silent_trace.txt ---
        private static volatile bool _stopTraceCleanup = false;
        private static Thread _traceCleanupThread;
        private static string _baseDirCached = string.Empty;

        [STAThread]
        static void Main(string[] args)
        {
            // 1) Resolve a pasta base do jogo (ajuste para sua lógica atual)
            string baseDir = ResolveBaseDir();
            _baseDirCached = baseDir;

            // 2) Inicia supressão do trace (não deixa o arquivo existir)
            StartTraceSuppression(baseDir);

            try
            {
                // 3) Lança o FFVIII de forma silenciosa (sem UI do launcher)
                LaunchFf8(baseDir);
            }
            finally
            {
                // 4) Limpeza final
                StopTraceSuppression();
            }
        }

        static string ResolveBaseDir()
        {
            // IMPLEMENTE aqui a sua lógica atual para achar a pasta do FFVIII Remastered
            // Exemplo básico: pasta do executável atual
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            // Se o seu jogo está um nível acima, por exemplo:
            // return Path.GetFullPath(Path.Combine(exeDir, ".."));
            return exeDir;
        }

        static void LaunchFf8(string baseDir)
        {
            try
            {
                // Ajuste para apontar para o FFVIII.exe dentro do baseDir
                string ff8Path = Path.Combine(baseDir, "FFVIII.exe");
                if (!File.Exists(ff8Path))
                {
                    // Tente subir um nível se necessário
                    string alt = Path.Combine(
                        Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar))?.FullName ?? baseDir,
                        "FFVIII.exe");
                    if (File.Exists(alt)) ff8Path = alt;
                }

                if (!File.Exists(ff8Path))
                    throw new FileNotFoundException("FFVIII.exe não encontrado.", ff8Path);

                var psi = new ProcessStartInfo
                {
                    FileName = ff8Path,
                    WorkingDirectory = Path.GetDirectoryName(ff8Path) ?? baseDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var proc = Process.Start(psi))
                {
                    // Opcional: ocultar eventuais janelas de console rapidamente
                    if (proc != null)
                    {
                        var until = DateTime.UtcNow.AddSeconds(15);
                        while (!proc.HasExited && DateTime.UtcNow < until)
                        {
                            try { HideWindowByPid(proc.Id, "FFVIII.exe"); } catch { }
                            Thread.Sleep(300);
                        }
                    }
                }
            }
            catch
            {
                // Evite cair; não propague erro para não travar o usuário
            }
        }

        // === Ocultar janela (se necessário) ===
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        private const int SW_HIDE = 0;

        static void HideWindowByPid(int pid, string titleNeedle)
        {
            EnumWindows((h, _) =>
            {
                try
                {
                    if (!IsWindowVisible(h)) return true;
                    GetWindowThreadProcessId(h, out uint wPid);
                    if (wPid != (uint)pid) return true;

                    var len = GetWindowTextLength(h);
                    var sb = new System.Text.StringBuilder(len + 1);
                    GetWindowText(h, sb, sb.Capacity);
                    var title = sb.ToString();

                    if (string.IsNullOrEmpty(titleNeedle) || title.IndexOf(titleNeedle, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ShowWindow(h, SW_HIDE);
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
        }

        // === Supressão do eden_silent_trace.txt ===
        static void StartTraceSuppression(string baseDir)
        {
            try
            {
                string tracePath = Path.Combine(baseDir, "eden_silent_trace.txt");
                try { if (File.Exists(tracePath)) File.Delete(tracePath); } catch { }

                _stopTraceCleanup = false;
                _traceCleanupThread = new Thread(() =>
                {
                    var deadline = DateTime.UtcNow.AddMinutes(2);
                    while (!_stopTraceCleanup && DateTime.UtcNow < deadline)
                    {
                        try
                        {
                            if (File.Exists(tracePath))
                            {
                                try
                                {
                                    var attrs = File.GetAttributes(tracePath);
                                    if ((attrs & FileAttributes.ReadOnly) != 0)
                                        File.SetAttributes(tracePath, attrs & ~FileAttributes.ReadOnly);
                                }
                                catch { }
                                File.Delete(tracePath);
                            }
                        }
                        catch { }
                        Thread.Sleep(200);
                    }
                })
                { IsBackground = true, Name = "EdenSilentTraceSuppressor" };
                _traceCleanupThread.Start();

                AppDomain.CurrentDomain.ProcessExit += (_, __) => StopTraceSuppression();
                AppDomain.CurrentDomain.DomainUnload += (_, __) => StopTraceSuppression();
            }
            catch { }
        }

        static void StopTraceSuppression()
        {
            try
            {
                _stopTraceCleanup = true;
                if (_traceCleanupThread != null && _traceCleanupThread.IsAlive)
                    _traceCleanupThread.Join(1000);

                if (!string.IsNullOrEmpty(_baseDirCached))
                {
                    try
                    {
                        string p = Path.Combine(_baseDirCached, "eden_silent_trace.txt");
                        if (File.Exists(p)) File.Delete(p);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
