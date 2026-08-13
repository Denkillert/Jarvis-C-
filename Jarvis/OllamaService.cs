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
            Ты отвечаешь ТОЛЬКО валидным JSON-объектом. Никакого markdown, никаких слов до или после фигурных скобок.

            Структура твоего ответа:
            {{
              ""text"": ""Краткий ответ пользователю на русском языке."",
              ""action"": ""имя_команды_или_пустая_строка"",
              ""parameters"": {{}} 
            }}

            ДОСТУПНЫЕ ДЕЙСТВИЯ (action):
            {_commandManager.GetCommandsDescription()}

            КРИТИЧЕСКИ ВАЖНОЕ ПРАВИЛО ДЛЯ open_app И close_app:
            В параметре ""name"" указывай РЕАЛЬНОЕ имя процесса (exe-файла). Вот справочник:
            - Калькулятор → ""CalculatorApp.exe"" (НЕ calc.exe!)
            - Блокнот → ""notepad.exe""
            - Проводник → ""explorer.exe""
            - Диспетчер задач → ""Taskmgr.exe""
            - Discord → ""Discord.exe""
            - Steam → ""steam.exe""
            - Telegram → ""Telegram.exe""
            - Браузер Edge → ""msedge.exe""
            - Браузер Chrome → ""chrome.exe""

            ПРИМЕРЫ ПРАВИЛЬНЫХ ОТВЕТОВ:

            Пользователь: ""Открой калькулятор""
            Ты: {{""text"": ""Запускаю калькулятор."", ""action"": ""open_app"", ""parameters"": {{""name"": ""CalculatorApp.exe""}}}}

            Пользователь: ""Закрой калькулятор""
            Ты: {{""text"": ""Закрываю калькулятор."", ""action"": ""close_app"", ""parameters"": {{""name"": ""CalculatorApp.exe""}}}}

            Пользователь: ""Открой блокнот""
            Ты: {{""text"": ""Открываю блокнот."", ""action"": ""open_app"", ""parameters"": {{""name"": ""notepad.exe""}}}}

            Пользователь: ""Закрой дискорд""
            Ты: {{""text"": ""Закрываю Discord."", ""action"": ""close_app"", ""parameters"": {{""name"": ""Discord.exe""}}}}";
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
                    format = "json",
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
                var ollamaResponse = JsonSerializer.Deserialize<OllamaApiResponse>(responseJson);

                if (ollamaResponse?.message?.content != null)
                {
                    string jsonContent = ollamaResponse.message.content.Trim();

                    if (jsonContent.StartsWith("```json")) jsonContent = jsonContent.Substring(7);
                    if (jsonContent.StartsWith("```")) jsonContent = jsonContent.Substring(3);
                    if (jsonContent.EndsWith("```")) jsonContent = jsonContent.Substring(0, jsonContent.Length - 3);
                    jsonContent = jsonContent.Trim();

                    var result = JsonSerializer.Deserialize<AgentResponse>(jsonContent);

                    if (result != null)
                    {
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

    public class AgentResponse
    {
        public string text { get; set; } = "";
        public string action { get; set; } = "";
        public JsonElement parameters { get; set; }
    }
}