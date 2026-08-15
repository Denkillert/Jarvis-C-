using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace Jarvis
{
    /// <summary>
    /// Универсальный резолвер приложений.
    /// Ищет среди РЕАЛЬНО установленных программ (Пуск + реестр Uninstall),
    /// понимает русские названия и опечатки через транслитерацию + нечёткий поиск.
    /// </summary>
    public static class AppResolver
    {
        private static readonly Dictionary<string, string> _cache = new();
        private static List<(string Normalized, string Path)> _installedIndex = null;

        #region Словарь русских синонимов + частые ошибки распознавания

        private static readonly Dictionary<string, string> Aliases = new()
        {
            ["стим"] = "steam",
            ["стем"] = "steam",
            ["ским"] = "steam",
            ["steam"] = "steam",
            ["телеграм"] = "telegram",
            ["телега"] = "telegram",
            ["telegram"] = "telegram",
            ["тг"] = "telegram",
            ["дискорд"] = "discord",
            ["discord"] = "discord",
            ["хром"] = "chrome",
            ["chrome"] = "chrome",
            ["гугл"] = "chrome",
            ["googlechrome"] = "chrome",
            ["браузер"] = "msedge",
            ["edge"] = "msedge",
            ["эдж"] = "msedge",
            ["firefox"] = "firefox",
            ["фаерфокс"] = "firefox",
            ["огнелис"] = "firefox",
            ["калькулятор"] = "calc",
            ["calc"] = "calc",
            ["блокнот"] = "notepad",
            ["notepad"] = "notepad",
            ["проводник"] = "explorer",
            ["explorer"] = "explorer",
            ["диспетчерзадач"] = "taskmgr",
            ["taskmgr"] = "taskmgr",
            ["вотсап"] = "whatsapp",
            ["whatsapp"] = "whatsapp",
            ["скайп"] = "skype",
            ["skype"] = "skype",
            ["обс"] = "obs",
            ["obs"] = "obs",
            ["вконтакте"] = "vk",
            ["вк"] = "vk",
            ["вскод"] = "code",
            ["vscode"] = "code",
            ["code"] = "code",
            ["ворд"] = "winword",
            ["word"] = "winword",
            ["эксель"] = "excel",
            ["excel"] = "excel",
            ["аутлук"] = "outlook",
            ["outlook"] = "outlook",
            ["почта"] = "outlook",
            ["spotify"] = "spotify",
            ["спотифай"] = "spotify",
            ["тимс"] = "msteams",
            ["teams"] = "msteams",
            ["тим"] = "msteams",
        };

        private static readonly Dictionary<string, string[]> CommonPaths = new()
        {
            ["steam"] = new[]
            {
                @"C:\Program Files (x86)\Steam\steam.exe",
                @"C:\Program Files\Steam\steam.exe",
                @"D:\Steam\steam.exe",
                @"D:\Games\Steam\steam.exe",
                @"E:\Steam\steam.exe",
            },
            ["telegram"] = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Telegram", "Telegram.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Telegram Desktop", "Telegram.exe"),
            },
        };

        #endregion

        public static string Resolve(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput)) return null;

            var norm = Normalize(userInput);
            if (_cache.TryGetValue(norm, out var cached))
                return File.Exists(cached) ? cached : null;

            var canonical = ToCanonical(userInput);

            string path = null;
            if (canonical != null)
                path = FindInAppPaths(canonical) ?? FindInCommonPaths(canonical);

            // Универсальный поиск среди установленных (Пуск + Uninstall)
            path ??= FindInstalled(canonical ?? userInput);
            path ??= canonical != null ? FindInPath(canonical) : null;

            if (path != null) _cache[norm] = path;
            return path;
        }

        public static string GetProcessName(string userInput)
        {
            var path = Resolve(userInput);
            if (path != null) return Path.GetFileNameWithoutExtension(path);
            return ToCanonical(userInput) ?? Path.GetFileNameWithoutExtension(userInput);
        }

        public static string ToCanonical(string input)
        {
            var norm = Normalize(input);
            if (norm.Length == 0) return null;
            if (Aliases.TryGetValue(norm, out var canonical)) return canonical;

            string best = null;
            int bestDist = int.MaxValue;
            foreach (var kv in Aliases)
            {
                int allowed = kv.Key.Length <= 5 ? 1 : 2;
                if (Math.Abs(kv.Key.Length - norm.Length) > allowed) continue;
                int d = Levenshtein(norm, kv.Key);
                if (d <= allowed && d < bestDist)
                {
                    bestDist = d;
                    best = kv.Value;
                }
            }
            return best;
        }

        #region Источники поиска

        private static string FindInAppPaths(string name)
        {
            var roots = new[]
            {
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            };
            foreach (var root in roots)
            {
                if (root == null) continue;
                using (root)
                {
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        var keyName = Normalize(Path.GetFileNameWithoutExtension(sub));
                        if (keyName.Length == 0 || MatchScore(keyName, name) < 0) continue;

                        using var key = root.OpenSubKey(sub);
                        var path = key?.GetValue("") as string;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            return path;
                    }
                }
            }
            return null;
        }

        /// <summary>Универсальный поиск: ярлыки Пуска + реестр Uninstall, строгий скоринг</summary>
        private static string FindInstalled(string query)
        {
            if (_installedIndex == null) BuildInstalledIndex();

            var norm = Normalize(query);
            var translit = Transliterate(norm);
            var variants = new[] { norm, translit }.Where(v => v.Length >= 3).Distinct().ToList();
            if (variants.Count == 0) return null;

            string bestPath = null;
            int bestScore = int.MaxValue;

            foreach (var (name, path) in _installedIndex)
            {
                foreach (var v in variants)
                {
                    int score = MatchScore(name, v);
                    if (score >= 0 && score < bestScore)
                    {
                        bestScore = score;
                        bestPath = path;
                    }
                }
            }

            if (bestPath == null) return null;

            if (bestPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var target = ResolveShortcutTarget(bestPath);
                return target != null && File.Exists(target) ? target : null;
            }
            return File.Exists(bestPath) ? bestPath : null;
        }

        private static void BuildInstalledIndex()
        {
            _installedIndex = new List<(string, string)>();

            // Ярлыки меню "Пуск"
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            };
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    foreach (var lnk in Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories))
                    {
                        var name = Normalize(Path.GetFileNameWithoutExtension(lnk));
                        if (name.Length > 0) _installedIndex.Add((name, lnk));
                    }
                }
                catch { }
            }

            // Реестр Uninstall
            var roots = new[]
            {
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            };
            foreach (var root in roots)
            {
                if (root == null) continue;
                using (root)
                {
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = root.OpenSubKey(sub);
                            var displayName = key?.GetValue("DisplayName") as string;
                            if (string.IsNullOrEmpty(displayName)) continue;
                            var normName = Normalize(displayName);
                            if (normName.Length == 0) continue;

                            string exe = null;
                            var installLocation = key.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                foreach (var f in Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly))
                                {
                                    var exeName = Normalize(Path.GetFileNameWithoutExtension(f));
                                    if (MatchScore(exeName, normName) >= 0) { exe = f; break; }
                                }
                            }
                            if (exe == null)
                            {
                                var icon = (key.GetValue("DisplayIcon") as string)?.Split(',')[0];
                                if (!string.IsNullOrEmpty(icon) && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon))
                                    exe = icon;
                            }
                            if (exe != null) _installedIndex.Add((normName, exe));
                        }
                        catch { }
                    }
                }
            }
        }

        private static string ResolveShortcutTarget(string lnkPath)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                return shortcut.TargetPath as string;
            }
            catch
            {
                return null;
            }
        }

        private static string FindInCommonPaths(string name)
        {
            if (CommonPaths.TryGetValue(name, out var paths))
            {
                foreach (var p in paths)
                    if (File.Exists(p)) return p;
            }
            return null;
        }

        private static string FindInPath(string name)
        {
            var exeName = name + ".exe";
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir.Trim(), exeName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        #endregion

        #region Строгий скоринг совпадений

        /// <summary>
        /// 0 = точное, 1 = префикс, 10+d = нечёткое. -1 = НЕ совпадение.
        /// "msteams" vs "steam" → -1 (больше никаких ложных Contains!)
        /// </summary>
        private static int MatchScore(string entry, string query)
        {
            if (entry.Length == 0 || query.Length == 0) return -1;
            if (entry == query) return 0;

            // Префикс: "telegramdesktop" начинается с "telegram"
            if (query.Length >= 4 && entry.StartsWith(query)) return 1;
            if (entry.Length >= 4 && query.StartsWith(entry)) return 1;

            // Нечёткое: ограниченная дистанция Левенштейна
            int maxLen = Math.Max(entry.Length, query.Length);
            int minLen = Math.Min(entry.Length, query.Length);
            int allowed = Math.Max(1, minLen / 3);
            if (maxLen - minLen > allowed) return -1;

            int d = Levenshtein(query, entry);
            return d <= allowed ? 10 + d : -1;
        }

        #endregion

        #region Утилиты

        private static string Normalize(string s) =>
            s == null ? "" : new string(s.ToLower().Where(char.IsLetterOrDigit).ToArray());

        private static string Transliterate(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                sb.Append(c switch
                {
                    'а' => "a",
                    'б' => "b",
                    'в' => "v",
                    'г' => "g",
                    'д' => "d",
                    'е' => "e",
                    'ё' => "e",
                    'ж' => "zh",
                    'з' => "z",
                    'и' => "i",
                    'й' => "y",
                    'к' => "k",
                    'л' => "l",
                    'м' => "m",
                    'н' => "n",
                    'о' => "o",
                    'п' => "p",
                    'р' => "r",
                    'с' => "s",
                    'т' => "t",
                    'у' => "u",
                    'ф' => "f",
                    'х' => "h",
                    'ц' => "c",
                    'ч' => "ch",
                    'ш' => "sh",
                    'щ' => "sch",
                    'ъ' => "",
                    'ы' => "y",
                    'ь' => "",
                    'э' => "e",
                    'ю' => "yu",
                    'я' => "ya",
                    _ => c.ToString()
                });
            }
            return sb.ToString();
        }

        private static int Levenshtein(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }

        #endregion
    }
}