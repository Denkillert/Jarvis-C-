using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jarvis
{
    public static class AppFinder
    {
        private static readonly Dictionary<string, string> _appCache = new();

        // Системные команды Windows
        private static readonly Dictionary<string, string> SystemCommands = new()
        {
            { "калькулятор", "calc.exe" },
            { "кальк", "calc.exe" },
            { "calc", "calc.exe" },
            { "блокнот", "notepad.exe" },
            { "notepad", "notepad.exe" },
            { "проводник", "explorer.exe" },
            { "explorer", "explorer.exe" },
            { "диспетчер задач", "taskmgr.exe" },
            { "taskmgr", "taskmgr.exe" },
            { "cmd", "cmd.exe" },
            { "командная строка", "cmd.exe" },
            { "терминал", "wt.exe" },
            { "powershell", "powershell.exe" },
            { "браузер", GetDefaultBrowserPath() },
            { "edge", "msedge.exe" },
            { "chrome", "chrome.exe" }
        };

        public static string FindAppPath(string appName)
        {
            var lowerName = appName.ToLower().Trim();

            // Проверяем кэш
            if (_appCache.TryGetValue(lowerName, out var cachedPath))
            {
                System.Diagnostics.Debug.WriteLine($"[AppFinder] Из кэша: {lowerName} → {cachedPath}");
                return cachedPath;
            }

            // 1. Проверяем системные команды
            if (SystemCommands.TryGetValue(lowerName, out var sysCmd))
            {
                _appCache[lowerName] = sysCmd;
                System.Diagnostics.Debug.WriteLine($"[AppFinder] Системная команда: {lowerName} → {sysCmd}");
                return sysCmd;
            }

            // 2. Ищем в реестре Uninstall
            string foundPath = FindInUninstallRegistry(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                return foundPath;
            }

            // 3. Ищем в App Paths
            foundPath = FindInAppPathsRegistry(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                return foundPath;
            }

            // 4. Ищем ярлыки в меню Пуск
            foundPath = FindShortcutInStartMenu(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                return foundPath;
            }

            // 5. Ищем на рабочем столе
            foundPath = FindShortcutOnDesktop(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                return foundPath;
            }

            // 6. Ищем в Program Files
            foundPath = FindInProgramFolders(lowerName);
            if (!string.IsNullOrEmpty(foundPath))
            {
                _appCache[lowerName] = foundPath;
                return foundPath;
            }

            // 7. Пробуем просто вернуть имя (вдруг это системная команда)
            System.Diagnostics.Debug.WriteLine($"[AppFinder] Возвращаю как есть: {lowerName}");
            return lowerName.EndsWith(".exe") ? lowerName : $"{lowerName}.exe";
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

                            System.Diagnostics.Debug.WriteLine($"[Registry] Найдено: {displayName}");

                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                var allExeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.AllDirectories);

                                var badKeywords = new[] { "uninstall", "update", "setup", "helper", "crashpad", "service" };

                                var candidates = allExeFiles
                                    .Where(f =>
                                    {
                                        var name = Path.GetFileNameWithoutExtension(f).ToLower();
                                        return !badKeywords.Any(bad => name.Contains(bad));
                                    })
                                    .ToList();

                                var bestMatch = candidates
                                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLower().Contains(appName));

                                if (bestMatch != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Registry] Лучшее совпадение: {bestMatch}");
                                    return bestMatch;
                                }

                                if (candidates.Count > 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Registry] Берём первый: {candidates[0]}");
                                    return candidates[0];
                                }
                            }

                            if (!string.IsNullOrEmpty(displayIcon))
                            {
                                var iconPath = displayIcon.Split(',')[0].Trim();
                                if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                    File.Exists(iconPath) &&
                                    !Path.GetFileNameWithoutExtension(iconPath).ToLower().Contains("uninstall"))
                                {
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
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[App Paths] {appName} → {path}");
                                        return path;
                                    }
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

                    var shortcuts = Directory.GetFiles(basePath, $"*{appName}*.lnk", SearchOption.AllDirectories);
                    foreach (var shortcut in shortcuts)
                    {
                        var targetPath = GetShortcutTarget(shortcut);
                        if (!string.IsNullOrEmpty(targetPath) &&
                            File.Exists(targetPath) &&
                            targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Start Menu] {appName} → {targetPath}");
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

                    var shortcuts = Directory.GetFiles(desktopPath, $"*{appName}*.lnk");
                    foreach (var shortcut in shortcuts)
                    {
                        var targetPath = GetShortcutTarget(shortcut);
                        if (!string.IsNullOrEmpty(targetPath) &&
                            File.Exists(targetPath) &&
                            targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Desktop] {appName} → {targetPath}");
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
                    var appDirs = Directory.GetDirectories(basePath, $"*{appName}*", SearchOption.TopDirectoryOnly);

                    foreach (var dir in appDirs)
                    {
                        var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
                        foreach (var exe in exeFiles.Take(5))
                        {
                            if (Path.GetFileNameWithoutExtension(exe).ToLower().Contains(appName))
                            {
                                System.Diagnostics.Debug.WriteLine($"[Program Files] {appName} → {exe}");
                                return exe;
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static string GetDefaultBrowserPath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
                {
                    var progId = key?.GetValue("ProgId") as string;
                    if (!string.IsNullOrEmpty(progId))
                    {
                        using (var browserKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command"))
                        {
                            var command = browserKey?.GetValue("") as string;
                            if (!string.IsNullOrEmpty(command))
                            {
                                command = command.Replace("\"", "");
                                var parts = command.Split(' ');
                                return parts[0];
                            }
                        }
                    }
                }
            }
            catch { }

            return "explorer.exe";
        }

        private static string GetShortcutTarget(string shortcutPath)
        {
            try
            {
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

        public static void ClearCache()
        {
            _appCache.Clear();
        }
    }
}