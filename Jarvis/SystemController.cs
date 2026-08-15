using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Jarvis
{
    public static class SystemController
    {
        // WinAPI для управления окнами
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        // WinAPI для управления громкостью
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_VOLUME_UP = 0x0a;
        private const int APPCOMMAND_VOLUME_DOWN = 0x09;
        private const int APPCOMMAND_VOLUME_MUTE = 0x08;

        private const int SW_RESTORE = 9;

        // ===== ОТКРЫТЬ ПРИЛОЖЕНИЕ =====
        // ===== ОТКРЫТЬ ПРИЛОЖЕНИЕ (умный поиск пути) =====
        public static string OpenApp(string appName)
        {
            try
            {
                // 🔥 Сначала ищем реальный путь: реестр → ярлыки → папки
                string path = AppResolver.Resolve(appName);

                if (path != null)
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    return $"Запустил {Path.GetFileNameWithoutExtension(path)}.";
                }

                // Запасной вариант: системные утилиты (notepad, calc, mspaint)
                var shellName = appName.EndsWith(".exe") ? appName : appName + ".exe";
                Process.Start(new ProcessStartInfo { FileName = shellName, UseShellExecute = true });
                return $"{appName} запущен.";
            }
            catch
            {
                return $"Не нашёл приложение '{appName}' на этом компьютере. Проверь, что оно установлено.";
            }
        }

        // ===== РАЗВЕРНУТЬ =====
        public static string FocusApp(string appName)
        {
            try
            {
                var processName = AppResolver.GetProcessName(appName);
                var processes = GetProcessesByNameSmart(processName);

                if (processes.Length == 0)
                    return OpenApp(appName); // Не запущено — запускаем

                foreach (var proc in processes)
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        if (IsIconic(proc.MainWindowHandle))
                            ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                        return $"{appName} развёрнут.";
                    }
                }

                return OpenApp(appName);
            }
            catch (Exception ex)
            {
                return $"Не удалось развернуть {appName}: {ex.Message}";
            }
        }

        // ===== МЯГКО ЗАКРЫТЬ =====
        public static string CloseApp(string appName)
        {
            try
            {
                var processName = AppResolver.GetProcessName(appName);
                var processes = GetProcessesByNameSmart(processName);

                if (processes.Length == 0) return $"{appName} не запущен.";

                int closed = 0;
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.MainWindowHandle != IntPtr.Zero)
                            proc.CloseMainWindow();
                        else
                            proc.Kill();
                        closed++;
                    }
                    catch { }
                }
                return $"Закрыто {closed} экземпляр(ов) {appName}.";
            }
            catch (Exception ex)
            {
                return $"Не удалось закрыть {appName}: {ex.Message}";
            }
        }

        // ===== ПРИНУДИТЕЛЬНО УБИТЬ =====
        public static string ForceCloseApp(string appName)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(appName);
                var processes = GetProcessesByNameSmart(processName);

                if (processes.Length == 0) return $"{appName} не запущен.";

                int killed = 0;
                foreach (var proc in processes)
                {
                    try
                    {
                        proc.Kill();
                        killed++;
                    }
                    catch { }
                }
                return $"Убито {killed} процесс(ов) {appName}.";
            }
            catch (Exception ex)
            {
                return $"Не удалось убить {appName}: {ex.Message}";
            }
        }

        // Получить все окна процесса
        private static IntPtr[] GetProcessWindows(int processId)
        {
            var windows = new System.Collections.Generic.List<IntPtr>();

            EnumWindows((hWnd, lParam) =>
            {
                uint windowProcessId = 0;
                GetWindowThreadProcessId(hWnd, out windowProcessId);

                if (windowProcessId == processId && IsWindowVisible(hWnd))
                {
                    windows.Add(hWnd);
                }

                return true;
            }, IntPtr.Zero);

            return windows.ToArray();
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // ===== УМНЫЙ ПОИСК ПРОЦЕССОВ =====
        private static Process[] GetProcessesByNameSmart(string processName)
        {
            var processes = Process.GetProcessesByName(processName);

            if (processes.Length == 0 &&
                (processName.Equals("calc", StringComparison.OrdinalIgnoreCase) ||
                 processName.Equals("calculatorapp", StringComparison.OrdinalIgnoreCase)))
            {
                processes = Process.GetProcessesByName("CalculatorApp");
            }

            return processes;
        }


        // ===== ОТКРЫТЬ URL =====
        public static string OpenUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return $"Открыл {url}.";
            }
            catch (Exception ex)
            {
                return $"Не удалось открыть: {ex.Message}";
            }
        }

        // ===== ВЫПОЛНИТЬ КОМАНДУ CMD =====
        public static string RunCmd(string command)
        {
            var dangerousKeywords = new[]
            {
        "del ", "del.", "del ", "erase ", "format ", "rd ", "rmdir ",
        "shutdown ", "taskkill ", "fsutil ", "reg delete", "attrib ",
        "rm ", "mv ", "cp "
    };

            if (dangerousKeywords.Any(k => command.ToLower().Contains(k)))
            {
                return $"⛔ ОПАСНАЯ КОМАНДА ЗАБЛОКИРОВАНА: '{command}'";
            }

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c chcp 65001 >nul & {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    process.WaitForExit(5000);
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    if (process.ExitCode == 0)
                        return string.IsNullOrWhiteSpace(output) ? "Выполнено." : output.Trim();
                    else
                        return $"Ошибка: {error}";
                }
            }
            catch (Exception ex)
            {
                return $"Ошибка выполнения: {ex.Message}";
            }
        }

        // ===== СИСТЕМНЫЕ КОМАНДЫ =====
        public static string Shutdown()
        {
            Process.Start("shutdown", "/s /t 30");
            return "Компьютер выключится через 30 секунд.";
        }

        public static string Restart()
        {
            Process.Start("shutdown", "/r /t 30");
            return "Компьютер перезагрузится через 30 секунд.";
        }

        public static string LockScreen()
        {
            Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
            return "Экран заблокирован.";
        }

        // ===== ГРОМКОСТЬ ЧЕРЕЗ WinAPI =====
        public static string VolumeUp()
        {
            SendMessageW(IntPtr.Zero, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)(APPCOMMAND_VOLUME_UP << 16));
            return "Громкость увеличена.";
        }

        public static string VolumeDown()
        {
            SendMessageW(IntPtr.Zero, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)(APPCOMMAND_VOLUME_DOWN << 16));
            return "Громкость уменьшена.";
        }

        public static string VolumeMute()
        {
            SendMessageW(IntPtr.Zero, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)(APPCOMMAND_VOLUME_MUTE << 16));
            return "Звук переключен.";
        }

        // ===== ВРЕМЯ И ДАТА =====
        public static string GetTime()
        {
            return DateTime.Now.ToString("HH:mm");
        }

        public static string GetDate()
        {
            return DateTime.Now.ToString("dd MMMM yyyy");
        }
    }
}