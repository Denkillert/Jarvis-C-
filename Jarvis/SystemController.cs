using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis
{
    public static class SystemController
    {
        // Приложения, которые обычно живут в трее
        private static readonly string[] TrayApps = { "discord", "steam", "telegram" };

        // WinAPI функции для работы с окнами и принудительного закрытия
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const uint WM_CLOSE = 0x0010;

        public static string OpenApp(string appName)
        {
            try
            {
                string fullPath = AppFinder.FindAppPath(appName);

                if (!File.Exists(fullPath) || !fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Ошибка: найденный файл не является исполняемым";
                }

                var processName = Path.GetFileNameWithoutExtension(fullPath).ToLower();
                var existingProcesses = Process.GetProcessesByName(processName);

                if (existingProcesses.Length > 0)
                {
                    foreach (var proc in existingProcesses)
                    {
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            if (IsIconic(proc.MainWindowHandle))
                            {
                                ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                            }
                            SetForegroundWindow(proc.MainWindowHandle);
                            return $"Приложение {appName} развёрнуто";
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[OpenApp] Процесс в трее, запускаю повторно для разворачивания");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = fullPath,
                        UseShellExecute = true
                    });
                    return $"Приложение {appName} разворачивается";
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });

                return $"Приложение {appName} запущено";
            }
            catch (Exception ex)
            {
                return $"Не удалось запустить {appName}: {ex.Message}";
            }
        }

        // ДОБАВЛЕН параметр forceKill
        public static string CloseApp(string appName, bool forceKill = false)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(appName).ToLower();
                var processes = Process.GetProcessesByName(processName);

                if (processes.Length == 0)
                {
                    return $"Приложение {appName} не запущено";
                }

                int closedCount = 0;
                bool isTrayApp = TrayApps.Any(t => processName.Contains(t));

                foreach (var proc in processes)
                {
                    try
                    {
                        if (forceKill)
                        {
                            // ПОЛНОЕ закрытие: убиваем процесс намертво
                            proc.Kill();
                            closedCount++;
                        }
                        else if (isTrayApp)
                        {
                            // Мягкое закрытие для трей-приложений (эмуляция крестика)
                            if (proc.MainWindowHandle != IntPtr.Zero)
                            {
                                PostMessage(proc.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                                closedCount++;
                            }
                            else
                            {
                                // Если окна нет, но процесс есть, пробуем стандартное закрытие
                                proc.CloseMainWindow();
                                closedCount++;
                            }
                        }
                        else
                        {
                            // Обычные приложения
                            if (!proc.CloseMainWindow())
                            {
                                proc.Kill();
                            }
                            closedCount++;
                        }
                    }
                    catch { }
                }

                if (forceKill)
                    return $"{appName} полностью закрыт (процесс завершён)";

                return isTrayApp
                    ? $"{appName} свёрнут в трей"
                    : $"Закрыто {closedCount} экземпляр(ов) {appName}";
            }
            catch (Exception ex)
            {
                return $"Не удалось закрыть {appName}: {ex.Message}";
            }
        }

        public static string OpenUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return $"Открыл {url}";
            }
            catch (Exception ex)
            {
                return $"Не удалось открыть: {ex.Message}";
            }
        }

        public static string LockScreen()
        {
            Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
            return "Экран заблокирован";
        }

        public static string Shutdown()
        {
            Process.Start("shutdown", "/s /t 60");
            return "Компьютер выключится через 60 секунд";
        }

        public static string CancelShutdown()
        {
            Process.Start("shutdown", "/a");
            return "Выключение отменено";
        }

        public static string Restart()
        {
            Process.Start("shutdown", "/r /t 60");
            return "Компьютер перезагрузится через 60 секунд";
        }

        public static string OpenExplorer(string path = "")
        {
            try
            {
                Process.Start("explorer.exe", path);
                return $"Проводник открыт: {path}";
            }
            catch (Exception ex)
            {
                return $"Не удалось открыть: {ex.Message}";
            }
        }

        public static string RunCmd(string command)
        {
            try
            {
                var dangerousKeywords = new[] { "del", "erase", "format", "rd", "rmdir", "shutdown", "taskkill", "fsutil" };
                if (dangerousKeywords.Any(k => command.ToLower().Contains(k)))
                {
                    return $"⛔ ОТКАЗ: Команда '{command}' заблокирована (опасное действие).";
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c chcp 65001 >nul & {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(processInfo))
                {
                    process.WaitForExit(10000);

                    if (process.ExitCode == 0)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        return string.IsNullOrWhiteSpace(output) ? "Команда выполнена (вывод пуст)." : output.Trim();
                    }
                    else
                    {
                        string error = process.StandardError.ReadToEnd();
                        return $"Ошибка: {error}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Критическая ошибка: {ex.Message}";
            }
        }

        public static string GetSystemInfo()
        {
            var os = Environment.OSVersion;
            var processorCount = Environment.ProcessorCount;
            var memory = GC.GetTotalMemory(false) / 1024 / 1024;

            return $"ОС: Windows {os.Version}\nПроцессоров: {processorCount}\nПамяти используется: {memory} МБ";
        }
    }
}