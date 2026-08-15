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

        public OllamaService(string model = "qwen2.5:3b")
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _model = model;
            _endpoint = "http://127.0.0.1:11434/api/chat"; // 127.0.0.1 надежнее чем localhost (как в их фиксах)
            _history.Add(new ChatMessage { role = "system", content = GetSystemPrompt() });
        }

        private string GetSystemPrompt()
        {
            // Адаптация их промпта: четкие инструкции для инструментов, никакой воды
            return @"Ты — локальный AI-агент. Твоя задача: анализировать запрос и возвращать ТОЛЬКО валидный JSON.
Никакого markdown, никаких пояснений вне JSON.

Структура ответа:
{
  ""text"": ""Краткий, естественный ответ пользователю на русском языке. Без слов-паразитов."",
  ""action"": ""имя_инструмента_или_пустая_строка"",
  ""parameters"": {}
}

ДОСТУПНЫЕ ИНСТРУМЕНТЫ (action):
1. ""open_app"": Запустить приложение. parameters: {""name"": ""точное_имя.exe""}
   - Калькулятор → ""CalculatorApp.exe""
   - Блокнот → ""notepad.exe""
   - Проводник → ""explorer.exe""
   - Discord → ""Discord.exe""
2. ""close_app"": Закрыть приложение. parameters: {""name"": ""точное_имя.exe""}
3. ""open_url"": Открыть сайт. parameters: {""url"": ""https://...""}

ПРИМЕРЫ:
Запрос: ""Открой калькулятор""
Ответ: {""text"": ""Запускаю калькулятор."", ""action"": ""open_app"", ""parameters"": {""name"": ""CalculatorApp.exe""}}

Запрос: ""Привет, как дела?""
Ответ: {""text"": ""Системы в норме. Готов к работе."", ""action"": """", ""parameters"": {}}";
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
                    format = "json" // Принудительный JSON режим Ollama
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_endpoint, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var ollamaResponse = JsonSerializer.Deserialize<OllamaApiResponse>(responseJson);

                if (ollamaResponse?.message?.content != null)
                {
                    string jsonContent = ollamaResponse.message.content.Trim();

                    // Очистка от markdown-оберток (частая проблема маленьких моделей)
                    if (jsonContent.StartsWith("```json")) jsonContent = jsonContent[7..];
                    if (jsonContent.StartsWith("```")) jsonContent = jsonContent[3..];
                    if (jsonContent.EndsWith("```")) jsonContent = jsonContent[..^3];

                    var result = JsonSerializer.Deserialize<AgentResponse>(jsonContent.Trim());
                    if (result != null)
                    {
                        if (!string.IsNullOrEmpty(result.text))
                            _history.Add(new ChatMessage { role = "assistant", content = result.text });
                        return result;
                    }
                }
                return new AgentResponse { text = "Не удалось получить ответ от агента." };
            }
            catch (Exception ex)
            {
                return new AgentResponse { text = $"Ошибка сети или агента: {ex.Message}" };
            }
        }

        private class ChatMessage { public string role { get; set; } = ""; public string content { get; set; } = ""; }
        private class OllamaApiResponse { public MessageData message { get; set; } }
        private class MessageData { public string role { get; set; } public string content { get; set; } }
    }
}