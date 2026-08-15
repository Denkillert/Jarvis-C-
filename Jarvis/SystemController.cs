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
        public static string OpenApp(string appName)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(appName);

                // Если уже запущено - просто запускаем exe снова (приложение само развернется)
                if (Process.GetProcessesByName(processName).Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SystemController] Уже запущено, запускаю снова для разворачивания: {appName}");
                }

                // Ищем полный путь
                string fullPath = FindExecutablePath(appName);

                if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                {
                    Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
                    return $"{appName} запущен.";
                }

                // Пробуем по имени
                Process.Start(new ProcessStartInfo { FileName = appName, UseShellExecute = true });
                return $"{appName} запущен.";
            }
            catch (Exception ex)
            {
                return $"Не удалось запустить {appName}: {ex.Message}";
            }
        }

        // ===== РАЗВЕРНУТЬ - ПРОСТО ЗАПУСКАЕМ EXE СНОВА =====
            public static string FocusApp(string appName)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(appName);
                var processes = Process.GetProcessesByName(processName);

                // Фикс для UWP калькулятора
                if (processes.Length == 0 &&
                    (processName.Equals("calc", StringComparison.OrdinalIgnoreCase) ||
                     processName.Equals("calculatorapp", StringComparison.OrdinalIgnoreCase)))
                {
                    processes = Process.GetProcessesByName("CalculatorApp");
                }

                if (processes.Length == 0)
                {
                    // Не запущено — запускаем
                    return OpenApp(appName);
                }

                // Запущено — ищем окно с MainWindowHandle
                foreach (var proc in processes)
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        SetForegroundWindow(proc.MainWindowHandle);
                        return $"{appName} развёрнут.";
                    }
                }

                // Если у всех процессов MainWindowHandle = null (как у Discord)
                // Просто запускаем exe снова — приложение само разберётся
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
                var processName = Path.GetFileNameWithoutExtension(appName);
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

        // ===== УНИВЕРСАЛЬНЫЙ ПОИСК EXE =====
        private static string FindExecutablePath(string appName)
        {
            var searchName = Path.GetFileNameWithoutExtension(appName).ToLower();

            // 1. App Paths реестр
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                {
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            if (subKeyName.ToLower().Contains(searchName))
                            {
                                using (var subKey = key.OpenSubKey(subKeyName))
                                {
                                    var path = subKey?.GetValue("") as string;
                                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                                        return path;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Uninstall реестр
            try
            {
                var registryPaths = new[]
                {
                    Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                    Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                    Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
                };

                foreach (var basePath in registryPaths)
                {
                    if (basePath == null) continue;

                    foreach (var subKeyName in basePath.GetSubKeyNames())
                    {
                        using (var subKey = basePath.OpenSubKey(subKeyName))
                        {
                            var displayName = subKey?.GetValue("DisplayName") as string;
                            var installLocation = subKey?.GetValue("InstallLocation") as string;
                            var displayIcon = subKey?.GetValue("DisplayIcon") as string;

                            if (string.IsNullOrEmpty(displayName) ||
                                !displayName.ToLower().Contains(searchName))
                                continue;

                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                                foreach (var exe in exeFiles)
                                {
                                    var exeName = Path.GetFileNameWithoutExtension(exe).ToLower();
                                    if (exeName.Contains(searchName) || searchName.Contains(exeName))
                                        return exe;
                                }
                            }

                            if (!string.IsNullOrEmpty(displayIcon))
                            {
                                var iconPath = displayIcon.Split(',')[0].Trim();
                                if (File.Exists(iconPath) && iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    return iconPath;
                            }
                        }
                    }
                    basePath?.Dispose();
                }
            }
            catch { }

            // 3. Program Files
            var searchPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };

            foreach (var basePath in searchPaths)
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    var matchingDirs = Directory.GetDirectories(basePath, $"*{searchName}*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in matchingDirs)
                    {
                        var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
                        foreach (var exe in exeFiles.Take(10))
                        {
                            var exeName = Path.GetFileNameWithoutExtension(exe).ToLower();
                            if (exeName.Contains(searchName) || searchName.Contains(exeName))
                                return exe;
                        }
                    }
                }
                catch { }
            }

            return null;
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