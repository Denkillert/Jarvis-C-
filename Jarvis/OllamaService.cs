using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jarvis
{
    public class OllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _endpoint;
        private readonly List<ChatMessage> _history = new();
        private readonly CommandManager _commandManager;

        public OllamaService(string model = "qwen2.5:3b")
        {
            _httpClient = new HttpClient();
            _model = model;
            _endpoint = "http://localhost:11434/api/chat";
            _commandManager = new CommandManager();

            _history.Add(new ChatMessage { role = "system", content = GetSystemPrompt() });
        }

        private string GetSystemPrompt()
        {
            return $@"Ты — Джарвис, интеллектуальный и лаконичный ассистент. 
Ты отвечаешь ТОЛЬКО валидным JSON-объектом следующей структуры:
{{
  ""text"": ""Краткий ответ пользователю на русском языке. Если выполняешь команду, напиши что-то вроде 'Выполняю'. Если команды нет, просто ответь на вопрос."",
  ""action"": ""имя_команды_из_списка_ниже_или_пустая_строка"",
  ""parameters"": {{}} 
}}

ДОСТУПНЫЕ ДЕЙСТВИЯ (action):
{_commandManager.GetCommandsDescription()}

ПРИМЕР 1 (просто разговор):
Пользователь: ""Привет""
Твой ответ: {{""text"": ""Привет! Системы в норме."", ""action"": """", ""parameters"": {{}}}}

ПРИМЕР 2 (действие):
Пользователь: ""Открой калькулятор""
Твой ответ: {{""text"": ""Запускаю калькулятор."", ""action"": ""open_calculator"", ""parameters"": {{}}}}

ПРИМЕР 3 (действие с параметром):
Пользователь: ""Какой у меня IP?""
Твой ответ: {{""text"": ""Проверяю настройки сети."", ""action"": ""run_cmd"", ""parameters"": {{""command"": ""ipconfig""}}}}

Пользователь: ""Запусти дискорд""
Ты: {{""text"": ""Запускаю Discord."", ""action"": ""open_app"", ""parameters"": {{""name"": ""discord""}}}}

Пользователь: ""Открой стим""
Ты: {{""text"": ""Открываю Steam."", ""action"": ""open_app"", ""parameters"": {{""name"": ""steam""}}}}

ВАЖНО: 
- Твой ответ ДОЛЖЕН быть ТОЛЬКО этим JSON. Никакого markdown, никаких слов до или после фигурных скобок.
- Если действие не требуется, поле ""action"" должно быть пустой строкой """".";
        }

        public async Task<AgentResponse> AskAsync(string question)
        {
            try
            {
                _history.Add(new ChatMessage { role = "user", content = question });

                var requestBody = new
                {
                    model = _model,
                    messages = _history,
                    stream = false,
                    format = "json", // Заставляет модель генерировать только JSON внутри content
                    options = new
                    {
                        num_ctx = 4096,
                        temperature = 0.1
                    }
                };

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_endpoint, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();

                // ЭТАП 1: Десериализуем обёртку ответа Ollama API
                var ollamaResponse = JsonSerializer.Deserialize<OllamaApiResponse>(responseJson);

                if (ollamaResponse?.message?.content != null)
                {
                    // ЭТАП 2: Извлекаем строку content (которая должна быть нашим JSON)
                    string jsonContent = ollamaResponse.message.content.Trim();

                    // На всякий случай чистим от markdown-обёрток, если модель всё же их добавила
                    if (jsonContent.StartsWith("```json")) jsonContent = jsonContent.Substring(7);
                    if (jsonContent.StartsWith("```")) jsonContent = jsonContent.Substring(3);
                    if (jsonContent.EndsWith("```")) jsonContent = jsonContent.Substring(0, jsonContent.Length - 3);
                    jsonContent = jsonContent.Trim();

                    // ЭТАП 3: Десериализуем чистый JSON в наш AgentResponse
                    var result = JsonSerializer.Deserialize<AgentResponse>(jsonContent);

                    if (result != null)
                    {
                        // Сохраняем в историю только текстовую часть, чтобы не засорять контекст
                        if (!string.IsNullOrEmpty(result.text))
                        {
                            _history.Add(new ChatMessage { role = "assistant", content = result.text });
                        }
                        return result;
                    }
                }

                return new AgentResponse { text = "Ошибка: не удалось получить ответ." };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] ИСКЛЮЧЕНИЕ: {ex.Message}");
                return new AgentResponse { text = $"Ошибка: {ex.Message}" };
            }
        }

        public void ClearHistory()
        {
            _history.Clear();
            _history.Add(new ChatMessage { role = "system", content = GetSystemPrompt() });
        }

        private class ChatMessage
        {
            public string role { get; set; } = "";
            public string content { get; set; } = "";
        }

        // Вспомогательный класс для парсинга сырого ответа от Ollama API
        private class OllamaApiResponse
        {
            public MessageData message { get; set; }
        }

        private class MessageData
        {
            public string role { get; set; }
            public string content { get; set; }
        }
    }

    // Публичный класс для использования в MainWindow
    public class AgentResponse
    {
        public string text { get; set; } = "";
        public string action { get; set; } = "";
        public JsonElement parameters { get; set; }
    }
}