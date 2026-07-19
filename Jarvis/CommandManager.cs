using System.IO;
using System.Text.Json;

namespace Jarvis
{
    public class CommandManager
    {
        private readonly List<CommandDefinition> _commands = new();
        private readonly Dictionary<string, string> _appAliases = new();

        public CommandManager()
        {
            LoadCommands();
        }

        private void LoadCommands()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Commands.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<CommandsData>(json);

                    if (data?.commands != null)
                    {
                        _commands.AddRange(data.commands);
                    }

                    // Загружаем алиасы приложений
                    if (data?.appAliases != null)
                    {
                        foreach (var kvp in data.appAliases)
                        {
                            _appAliases[kvp.Key.ToLower()] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки команд: {ex.Message}");
            }
        }

        public string GetCommandsDescription()
        {
            var descriptions = _commands.Select(c =>
                $"- {c.name}: {c.description}" +
                (c.parameters.Count > 0 ? $" (параметры: {string.Join(", ", c.parameters.Keys)})" : "")
            );
            return string.Join("\n", descriptions);
        }

        // Новый метод: получаем реальное имя процесса по названию от пользователя
        public string GetProcessName(string appName)
        {
            var lowerName = appName.ToLower().Trim();
            if (_appAliases.TryGetValue(lowerName, out var processName))
            {
                return processName;
            }
            // Если алиаса нет, пробуем использовать как есть (убираем расширение если есть)
            return Path.GetFileNameWithoutExtension(appName);
        }

        private class CommandsData
        {
            public Dictionary<string, string> appAliases { get; set; } = new();
            public List<CommandDefinition> commands { get; set; } = new();
        }

        public class CommandDefinition
        {
            public string name { get; set; } = "";
            public string description { get; set; } = "";
            public Dictionary<string, string> parameters { get; set; } = new();
            public List<string> examples { get; set; } = new();
        }
    }
}