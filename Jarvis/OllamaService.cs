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
            _endpoint = "http://127.0.0.1:11434/api/chat";
            _history.Add(new ChatMessage { role = "system", content = GetSystemPrompt() });
        }

        private string GetSystemPrompt()
        {
            return @"Ты — Джарвис, голосовой ассистент. Отвечай кратко на русском языке.

ЕСЛИ пользователь просит выполнить действие (открыть приложение, закрыть, запустить, узнать время/дату, открыть сайт) — отвечай СТРОГО в формате JSON:
{""action"": ""имя_команды"", ""parameters"": {""name"": ""имя.exe""}, ""text"": ""краткий ответ""}

ДОСТУПНЫЕ КОМАНДЫ:
- open_app: {""name"": ""CalculatorApp.exe""} (калькулятор), {""name"": ""Discord.exe""}, {""name"": ""Telegram.exe""}, {""name"": ""notepad.exe""}
- close_app: {""name"": ""имя.exe""}
- focus_app: {""name"": ""имя.exe""} (развернуть)
- open_url: {""url"": ""https://...""}
- get_time: {} (верни время в поле text)
- get_date: {} (верни дату в поле text)
- run_cmd: {""command"": ""команда""}

ИНАЧЕ — отвечай обычным текстом, без JSON.

ПРИМЕРЫ:
Пользователь: ""Привет""
Ты: Привет! Чем могу помочь?

Пользователь: ""Открой калькулятор""
Ты: {""action"": ""open_app"", ""parameters"": {""name"": ""CalculatorApp.exe""}, ""text"": ""Открываю калькулятор""}

Пользователь: ""Какое время?""
Ты: {""action"": ""get_time"", ""parameters"": {}, ""text"": ""Сейчас 14:30""}

Пользователь: ""Расскажи шутку""
Ты: Почему программисты путают Хэллоуин и Рождество? Потому что 31 OCT = 25 DEC";
        }

        public async Task<AgentResponse> AskAsync(string question)
        {
            try
            {
                // Ограничение истории
                if (_history.Count > 12)
                {
                    var systemMessage = _history[0];
                    _history.Clear();
                    _history.Add(systemMessage);
                }

                _history.Add(new ChatMessage { role = "user", content = question });

                var requestBody = new
                {
                    model = _model,
                    messages = _history,
                    stream = false,
                    options = new { temperature = 0.3 }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_endpoint, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var ollamaResponse = JsonSerializer.Deserialize<OllamaApiResponse>(responseJson);

                if (ollamaResponse?.message?.content != null)
                {
                    string reply = ollamaResponse.message.content.Trim();

                    // Очищаем от markdown
                    if (reply.StartsWith("```json")) reply = reply[7..];
                    if (reply.StartsWith("```")) reply = reply[3..];
                    if (reply.EndsWith("```")) reply = reply[..^3];
                    reply = reply.Trim();

                    // 🔥 ПРОВЕРЯЕМ: это JSON или обычный текст?
                    if (reply.StartsWith("{") && reply.EndsWith("}"))
                    {
                        try
                        {
                            var result = JsonSerializer.Deserialize<AgentResponse>(reply);
                            if (result != null)
                            {
                                _history.Add(new ChatMessage { role = "assistant", content = reply });
                                return result;
                            }
                        }
                        catch
                        {
                            // Не JSON — возвращаем как текст
                        }
                    }

                    // Обычный текст — нет команды
                    _history.Add(new ChatMessage { role = "assistant", content = reply });
                    return new AgentResponse { text = reply, action = "" };
                }

                return new AgentResponse { text = "Не удалось получить ответ." };
            }
            catch (Exception ex)
            {
                return new AgentResponse { text = $"Ошибка: {ex.Message}" };
            }
        }

        private class ChatMessage { public string role { get; set; } = ""; public string content { get; set; } = ""; }
        private class OllamaApiResponse { public MessageData message { get; set; } }
        private class MessageData { public string role { get; set; } public string content { get; set; } }
    }
}