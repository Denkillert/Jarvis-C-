using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jarvis
{
    public static class AppFinder
    {
        // Кэш найденных приложений
        private static readonly Dictionary<string, string> _appCache = new();

        public static string FindAppPath(string appName)
        {
            var lowerName = appName.ToLower().Trim();

            // Проверяем кэш
            if (_appCache.TryGetValue(lowerName, out var cachedPath))
            {
                System.Diagnostics.Debug.WriteLine($"[AppFinder] Найдено в кэше: {lowerName} → {cachedPath}");
                return cachedPath;
            }

            string foundPath = null;

            // 1. Системные приложения
            if (TryGetSystemApp(lowerName, out foundPath))
            {
                _appCache[lowerName] = foundPath;
                System.Diagnostics.Debug.WriteLine($"[AppFinder] Системное: {lowerName} → {foundPath}");
                return foundPath;
            }

            // 2. Ищем в реестре Uninstall
            foundPath = FindInUninstallRegistry(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                System.Diagnostics.Debug.WriteLine($"[AppFinder] Реестр Uninstall: {lowerName} → {foundPath}");
                return foundPath;
            }

            // 3. Ищем в App Paths
            foundPath = FindInAppPathsRegistry(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                System.Diagnostics.Debug.WriteLine($"[AppFinder] App Paths: {lowerName} → {foundPath}");
                return foundPath;
            }

            // 4. Ищем ярлыки в меню Пуск (ТОЛЬКО .exe, не .url)
            foundPath = FindShortcutInStartMenu(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                System.Diagnostics.Debug.WriteLine($"[AppFinder] Меню Пуск: {lowerName} → {foundPath}");
                return foundPath;
            }

            // 5. Ищем на рабочем столе (ТОЛЬКО .exe, не .url)
            foundPath = FindShortcutOnDesktop(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                System.Diagnostics.Debug.WriteLine($"[AppFinder] Рабочий стол: {lowerName} → {foundPath}");
                return foundPath;
            }

            // 6. Ищем в Program Files
            foundPath = FindInProgramFolders(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                System.Diagnostics.Debug.WriteLine($"[AppFinder] Program Files: {lowerName} → {foundPath}");
                return foundPath;
            }

            System.Diagnostics.Debug.WriteLine($"[AppFinder] НЕ НАЙДЕНО: {lowerName}");
            return appName;
        }

        private static bool TryGetSystemApp(string name, out string path)
        {
            var systemApps = new Dictionary<string, string>
            {
                { "калькулятор", "calc" },
                { "calculator", "calc" },
                { "блокнот", "notepad" },
                { "проводник", "explorer" },
                { "диспетчер задач", "taskmgr" },
                { "cmd", "cmd" },
                { "командная строка", "cmd" }
            };

            if (systemApps.TryGetValue(name, out path))
                return true;

            path = null;
            return false;
        }

        private static string FindInUninstallRegistry(string appName)
        {
            try
            {
                var registryPaths = new[]
                {
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                };

                foreach (var key in registryPaths)
                {
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            var displayName = subKey?.GetValue("DisplayName") as string;
                            var installLocation = subKey?.GetValue("InstallLocation") as string;
                            var displayIcon = subKey?.GetValue("DisplayIcon") as string;

                            if (string.IsNullOrEmpty(displayName) ||
                                !displayName.ToLower().Contains(appName))
                                continue;

                            System.Diagnostics.Debug.WriteLine($"[Registry] Найдено: {displayName} в {installLocation}");

                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                // Ищем exe рекурсивно во ВСЕХ подпапках
                                var allExeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.AllDirectories);

                                System.Diagnostics.Debug.WriteLine($"[Registry] Найдено exe файлов: {allExeFiles.Length}");

                                // Фильтруем: исключаем updater, uninstall, setup, helper, crashpad
                                var badKeywords = new[] { "uninstall", "update", "setup", "helper", "crashpad", "service" };

                                var candidates = allExeFiles
                                    .Where(f =>
                                    {
                                        var name = Path.GetFileNameWithoutExtension(f).ToLower();
                                        return !badKeywords.Any(bad => name.Contains(bad));
                                    })
                                    .ToList();

                                System.Diagnostics.Debug.WriteLine($"[Registry] Кандидатов после фильтрации: {candidates.Count}");

                                foreach (var c in candidates)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Registry] Кандидат: {c}");
                                }

                                // Приоритет 1: exe с именем приложения
                                var bestMatch = candidates
                                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLower().Contains(appName));

                                if (bestMatch != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Registry] Лучшее совпадение: {bestMatch}");
                                    return bestMatch;
                                }

                                // Приоритет 2: любой оставшийся exe
                                if (candidates.Count > 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Registry] Берём первый кандидат: {candidates[0]}");
                                    return candidates[0];
                                }
                            }

                            // DisplayIcon как запасной вариант (только .exe)
                            if (!string.IsNullOrEmpty(displayIcon))
                            {
                                var iconPath = displayIcon.Split(',')[0].Trim();
                                if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                    File.Exists(iconPath) &&
                                    !Path.GetFileNameWithoutExtension(iconPath).ToLower().Contains("update"))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Registry] Через DisplayIcon: {iconPath}");
                                    return iconPath;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Registry] Ошибка: {ex.Message}");
            }

            return null;
        }

        private static string FindInAppPathsRegistry(string appName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                {
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            if (subKeyName.ToLower().Contains(appName + ".exe") ||
                                subKeyName.ToLower().Contains(appName))
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

            return null;
        }

        private static string FindShortcutInStartMenu(string appName)
        {
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

                    // Ищем .lnk файлы
                    var shortcuts = Directory.GetFiles(basePath, $"*{appName}*.lnk", SearchOption.AllDirectories);
                    foreach (var shortcut in shortcuts)
                    {
                        var targetPath = GetShortcutTarget(shortcut);

                        // ВАЖНО: Проверяем что это .exe, а не .url (веб-ссылка)
                        if (!string.IsNullOrEmpty(targetPath) &&
                            File.Exists(targetPath) &&
                            targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            return targetPath;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static string FindShortcutOnDesktop(string appName)
        {
            try
            {
                var desktopPaths = new[]
                {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory))
        };

                foreach (var desktopPath in desktopPaths)
                {
                    if (!Directory.Exists(desktopPath)) continue;

                    // Ищем .lnk файлы
                    var shortcuts = Directory.GetFiles(desktopPath, $"*{appName}*.lnk");
                    foreach (var shortcut in shortcuts)
                    {
                        var targetPath = GetShortcutTarget(shortcut);

                        // Проверяем что это .exe
                        if (!string.IsNullOrEmpty(targetPath) &&
                            File.Exists(targetPath) &&
                            targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            return targetPath;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static string FindInProgramFolders(string appName)
        {
            var searchPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            foreach (var basePath in searchPaths)
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    // Ищем папку
                    var appDirs = Directory.GetDirectories(basePath, $"*{appName}*", SearchOption.TopDirectoryOnly);

                    foreach (var dir in appDirs)
                    {
                        var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
                        foreach (var exe in exeFiles.Take(5)) // Проверяем первые 5 exe
                        {
                            if (Path.GetFileNameWithoutExtension(exe).ToLower().Contains(appName))
                                return exe;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static string GetShortcutTarget(string shortcutPath)
        {
            try
            {
                // Используем WScript.Shell для чтения .lnk файлов
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;

                dynamic shell = Activator.CreateInstance(shellType);
                var shortcut = shell.CreateShortcut(shortcutPath);
                string targetPath = shortcut.TargetPath;

                return targetPath;
            }
            catch
            {
                return null;
            }
        }
    }
}