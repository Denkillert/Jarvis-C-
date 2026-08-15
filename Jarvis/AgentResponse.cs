using System.Text.Json;

namespace Jarvis
{
    // Строгий контракт, как в их agent-manager.ts
    public class AgentResponse
    {
        // Что Джарвис должен сказать вслух
        public string text { get; set; } = "";

        // Какой инструмент вызвать (open_app, close_app, open_url, или пусто)
        public string action { get; set; } = "";

        // Параметры для инструмента
        public JsonElement parameters { get; set; }
    }
}