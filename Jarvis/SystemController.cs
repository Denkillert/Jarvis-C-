using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Jarvis
{
    public static class SystemController
    {
        public static string OpenApp(string processName)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = processName,
                    UseShellExecute = true
                });
                return $"Приложение запущено";
            }
            catch (Exception ex)
            {
                return $"Не удалось запустить: {ex.Message}";
            }
        }

        public static string CloseApp(string appName)
        {
            try
            {
                var processName = appName.ToLower().Trim();

                // Для калькулятора и других UWP приложений
                if (processName.Contains("калькулятор") || processName.Contains("calculator") || processName == "calc")
                {
                    // Ищем процесс ApplicationFrameHost (хост для UWP приложений)
                    var frameHosts = Process.GetProcessesByName("ApplicationFrameHost");
                    foreach (var host in frameHosts)
                    {
                        // Проверяем, это калькулятор?
                        if (host.MainWindowTitle.Contains("Калькулятор") ||
                            host.MainWindowTitle.Contains("Calculator"))
                        {
                            host.Kill();
                            return "Калькулятор закрыт";
                        }
                    }
                    return "Калькулятор не найден";
                }

                // Для обычных приложений
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    return $"Приложение {appName} не запущено";
                }

                int closedCount = 0;
                foreach (var proc in processes)
                {
                    try
                    {
                        if (!proc.CloseMainWindow())
                        {
                            proc.Kill();
                        }
                        closedCount++;
                    }
                    catch { }
                }

                return $"Закрыто {closedCount} экземпляр(ов)";
            }
            catch (Exception ex)
            {
                return $"Не удалось закрыть: {ex.Message}";
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

        public static string GetSystemInfo()
        {
            var os = Environment.OSVersion;
            var processorCount = Environment.ProcessorCount;
            var memory = GC.GetTotalMemory(false) / 1024 / 1024;

            return $"ОС: Windows {os.Version}\nПроцессоров: {processorCount}\nПамяти используется: {memory} МБ";
        }
        public static string RunCmd(string command)
        {
            try
            {
                // ЧЕРНЫЙ СПИСОК: блокируем опасные команды
                var dangerousKeywords = new[] { "del", "erase", "format", "rd", "rmdir", "shutdown", "taskkill", "fsutil" };
                if (dangerousKeywords.Any(k => command.ToLower().Contains(k)))
                {
                    return $" ОТКАЗ: Команда '{command}' заблокирована (опасное действие).";
                }

                // Выполняем команду с UTF-8 кодировкой
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c chcp 65001 >nul & {command}", // Переключаем на UTF-8
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
                    process.WaitForExit(10000); // Ждем 10 секунд

                    if (process.ExitCode == 0)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        return string.IsNullOrWhiteSpace(output) ? "Команда выполнена (вывод пуст)." : output.Trim();
                    }
                    else
                    {
                        string error = process.StandardError.ReadToEnd();
                        return $"Ошибка выполнения: {error}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Критическая ошибка: {ex.Message}";
            }
        }
    }
}