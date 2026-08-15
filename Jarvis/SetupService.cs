using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace Jarvis
{
    /// <summary>
    /// Установщик v3: ВСЁ в папку проекта, рядом с exe.
    /// bin\Debug\net9.0-windows\Models\ru\...      ← Vosk (речь)
    /// bin\Debug\net9.0-windows\Models\ollama\...  ← Ollama (мозг)
    /// </summary>
    public static class SetupService
    {
        private const string VoskUrl = "https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip";
        private const string OllamaInstallerUrl = "https://ollama.com/download/OllamaSetup.exe";
        public const string LlmModel = "qwen2.5:3b";

        public const long NeedVosk = 300L * 1024 * 1024;      // 300 МБ
        public const long NeedFull = 4L * 1024 * 1024 * 1024; // 4 ГБ

        // 🔥 Всё в папке проекта
        public static string ModelsRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
        public static string VoskModelPath => Path.Combine(ModelsRoot, "ru", "vosk-model-small-ru-0.22");
        public static string OllamaModelsDir => Path.Combine(ModelsRoot, "ollama");

        /// <summary>True, если Ollama запущена ДЖАРВИСОМ, а не самим пользователем</summary>
        public static bool OllamaStartedByUs { get; private set; }

        public static bool IsVoskReady() =>
            Directory.Exists(VoskModelPath) &&
            Directory.GetFiles(VoskModelPath, "*", SearchOption.AllDirectories).Length > 0;

        public static long FreeSpaceHere()
        {
            try
            {
                var root = Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory);
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch { return 0; }
        }

        // ===== 1. РЕЧЬ (Vosk) =====
        public static async Task EnsureVoskAsync(Action<string> log, Action<int> progress)
        {
            if (IsVoskReady()) { log("Модель речи найдена."); return; }

            if (FreeSpaceHere() < NeedVosk)
                throw new Exception($"На диске {Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory)} нет 300 МБ для модели речи.");

            log("Скачиваю модель речи (~50 МБ)...");
            Directory.CreateDirectory(ModelsRoot);
            var zipPath = Path.Combine(ModelsRoot, "vosk.tmp.zip");

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var response = await client.GetAsync(VoskUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                using var net = await response.Content.ReadAsStreamAsync();
                using var file = File.Create(zipPath);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await net.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await file.WriteAsync(buffer, 0, n);
                    read += n;
                    if (total > 0) progress((int)(read * 100 / total));
                }
            }

            log("Распаковываю модель...");
            Directory.CreateDirectory(Path.Combine(ModelsRoot, "ru"));
            ZipFile.ExtractToDirectory(zipPath, Path.Combine(ModelsRoot, "ru"), true);
            File.Delete(zipPath);
            log("Модель речи установлена ✅");
        }

        // ===== 2. МОЗГ (Ollama) — стартует вместе с Джарвисом =====
        public static async Task EnsureOllamaAsync(Action<string> log)
        {
            Directory.CreateDirectory(OllamaModelsDir);

            // 🔥 Модели Ollama лежат в папке проекта, а не в C:\Users\...\.ollama
            Environment.SetEnvironmentVariable("OLLAMA_MODELS", OllamaModelsDir, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("OLLAMA_MODELS", OllamaModelsDir, EnvironmentVariableTarget.User);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            if (!await IsOllamaReadyAsync(client))
            {
                var exe = FindOllamaExe();
                if (exe == null)
                {
                    log("Скачиваю установщик Ollama...");
                    var setupPath = Path.Combine(ModelsRoot, "OllamaSetup.exe");
                    using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
                    using (var resp = await http.GetAsync(OllamaInstallerUrl, HttpCompletionOption.ResponseHeadersRead))
                    using (var net = await resp.Content.ReadAsStreamAsync())
                    using (var file = File.Create(setupPath))
                    {
                        await net.CopyToAsync(file);
                    }

                    log("Устанавливаю Ollama (тихо)...");
                    var install = Process.Start(new ProcessStartInfo
                    {
                        FileName = setupPath,
                        Arguments = "/VERYSILENT /NORESTART",
                        UseShellExecute = true
                    });
                    install.WaitForExit(300_000);
                    try { File.Delete(setupPath); } catch { }
                    exe = FindOllamaExe();
                }

                if (exe == null) { log("Не удалось установить Ollama — работаю без чата."); return; }

                // 🔥 ЗАПУСК OLLAMA ПРИ СТАРТЕ ДЖАРВИСА
                log("Запускаю Ollama...");
                StartServe(exe);
                OllamaStartedByUs = true;

                for (int i = 0; i < 30 && !await IsOllamaReadyAsync(client); i++)
                    await Task.Delay(500);
            }

            if (!await IsOllamaReadyAsync(client)) { log("Ollama не запустилась — работаю без чата."); return; }

            var tags = await client.GetStringAsync("http://127.0.0.1:11434/api/tags");
            if (!tags.Contains("qwen2.5"))
            {
                if (FreeSpaceHere() < NeedFull)
                {
                    log("Мало места для мозга (нужно ~4 ГБ) — лёгкий режим.");
                    return;
                }

                log("Скачиваю мозг qwen2.5:3b (~2 ГБ) в папку проекта...");
                var exe = FindOllamaExe();
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"pull {LlmModel}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                psi.Environment["OLLAMA_MODELS"] = OllamaModelsDir;
                var pull = Process.Start(psi);
                await pull.WaitForExitAsync();
                log("Мозг установлен ✅");
            }
        }

        private static void StartServe(string exe)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "serve",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.Environment["OLLAMA_MODELS"] = OllamaModelsDir;
            Process.Start(psi);
        }

        /// <summary>Убить Ollama при закрытии Джарвиса (если пользователь согласился)</summary>
        public static void StopOllama()
        {
            foreach (var name in new[] { "ollama", "ollama app" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(); } catch { }
                }
            }
        }

        public static async Task<bool> IsOllamaReadyAsync(HttpClient client)
        {
            try
            {
                var resp = await client.GetAsync("http://127.0.0.1:11434/api/tags");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private static string FindOllamaExe()
        {
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama.exe");
            if (File.Exists(local)) return local;

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(';'))
            {
                try
                {
                    var p = Path.Combine(dir.Trim(), "ollama.exe");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return null;
        }
    }
}