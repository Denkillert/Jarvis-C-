using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Jarvis
{
    public static class SystemController
    {
        public static string OpenApp(string appName)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(appName);

                // Проверяем, не запущено ли уже
                if (Process.GetProcessesByName(processName).Length > 0)
                {
                    return $"{appName} уже запущен.";
                }

                // 🔍 УНИВЕРСАЛЬНЫЙ ПОИСК
                string fullPath = FindExecutablePath(appName);

                if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[SystemController] Запускаю: {fullPath}");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = fullPath,
                        UseShellExecute = true
                    });
                    return $"{appName} запущен.";
                }

                // Если не нашли полный путь, пробуем запустить по имени
                // (для системных утилит и UWP приложений)
                System.Diagnostics.Debug.WriteLine($"[SystemController] Пытаюсь запустить по имени: {appName}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = appName,
                    UseShellExecute = true
                });
                return $"{appName} запущен.";
            }
            catch (Exception ex)
            {
                return $"Не удалось запустить {appName}: {ex.Message}";
            }
        }

        // 🔍 УНИВЕРСАЛЬНЫЙ МЕТОД ПОИСКА ЛЮБОГО EXE
        private static string FindExecutablePath(string appName)
        {
            var searchName = Path.GetFileNameWithoutExtension(appName).ToLower();

            // 1. Ищем в реестре App Paths (самый надежный способ)
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                {
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            if (subKeyName.ToLower().Contains(searchName) ||
                                Path.GetFileNameWithoutExtension(subKeyName).ToLower() == searchName)
                            {
                                using (var subKey = key.OpenSubKey(subKeyName))
                                {
                                    var path = subKey?.GetValue("") as string;
                                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[SystemController] Найдено в App Paths: {path}");
                                        return path;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Ищем в реестре Uninstall
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
                            var uninstallString = subKey?.GetValue("UninstallString") as string;

                            if (string.IsNullOrEmpty(displayName) ||
                                !displayName.ToLower().Contains(searchName))
                                continue;

                            System.Diagnostics.Debug.WriteLine($"[SystemController] Найдено в Uninstall: {displayName}");

                            // Проверяем InstallLocation
                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                                foreach (var exe in exeFiles)
                                {
                                    var exeName = Path.GetFileNameWithoutExtension(exe).ToLower();
                                    if (exeName.Contains(searchName) || searchName.Contains(exeName))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[SystemController] Найден EXE: {exe}");
                                        return exe;
                                    }
                                }
                            }

                            // Проверяем DisplayIcon
                            if (!string.IsNullOrEmpty(displayIcon))
                            {
                                var iconPath = displayIcon.Split(',')[0].Trim();
                                if (File.Exists(iconPath) && iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SystemController] Найден через DisplayIcon: {iconPath}");
                                    return iconPath;
                                }
                            }
                        }
                    }

                    basePath?.Dispose();
                }
            }
            catch { }

            // 3. Ищем в стандартных папках Program Files
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
                    // Ищем папки с похожим именем
                    var matchingDirs = Directory.GetDirectories(basePath, $"*{searchName}*", SearchOption.TopDirectoryOnly);

                    foreach (var dir in matchingDirs)
                    {
                        var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
                        foreach (var exe in exeFiles.Take(10)) // Берем первые 10 чтобы не тормозило
                        {
                            var exeName = Path.GetFileNameWithoutExtension(exe).ToLower();
                            if (exeName.Contains(searchName) || searchName.Contains(exeName))
                            {
                                System.Diagnostics.Debug.WriteLine($"[SystemController] Найден в папке {dir}: {exe}");
                                return exe;
                            }
                        }
                    }
                }
                catch { }
            }

            // 4. Ищем ярлыки в меню Пуск
            try
            {
                var startMenuPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
                };

                foreach (var basePath in startMenuPaths)
                {
                    if (!Directory.Exists(basePath)) continue;

                    var shortcuts = Directory.GetFiles(basePath, $"*{searchName}*.lnk", SearchOption.AllDirectories);
                    foreach (var shortcut in shortcuts)
                    {
                        try
                        {
                            var shellType = Type.GetTypeFromProgID("WScript.Shell");
                            if (shellType != null)
                            {
                                dynamic shell = Activator.CreateInstance(shellType);
                                var shortcutObj = shell.CreateShortcut(shortcut);
                                string targetPath = shortcutObj.TargetPath;

                                if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SystemController] Найден через ярлык: {targetPath}");
                                    return targetPath;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            System.Diagnostics.Debug.WriteLine($"[SystemController] Не найдено: {appName}");
            return null;
        }

        public static string CloseApp(string appName, bool forceKill = false)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(appName);
                var processes = Process.GetProcessesByName(processName);

                // Фикс для UWP калькулятора
                if (processes.Length == 0 && (processName.Equals("calc", StringComparison.OrdinalIgnoreCase) ||
                                              processName.Equals("calculatorapp", StringComparison.OrdinalIgnoreCase)))
                {
                    processes = Process.GetProcessesByName("CalculatorApp");
                }

                if (processes.Length == 0) return $"{appName} не запущен.";

                int closed = 0;
                foreach (var proc in processes)
                {
                    try
                    {
                        if (forceKill)
                        {
                            proc.Kill();
                        }
                        else
                        {
                            if (!proc.CloseMainWindow())
                            {
                                System.Threading.Thread.Sleep(500);
                                if (!proc.HasExited) proc.Kill();
                            }
                        }
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
    }
}