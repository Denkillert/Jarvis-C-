using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jarvis
{
    /// <summary>
    /// Улучшает распознанный текст: удаляет слова-паразиты,
    /// добавляет пунктуацию и исправляет грамматику через LLM.
    /// </summary>
    public class TextEnhancer
    {
        private readonly OllamaService _ollama;

        private readonly HashSet<string> _fillerWords = new()
        {
            "эээ", "э-э-э", "ааа", "гм", "ммм", "хм",
            "как бы", "типа", "ну", "вот", "значит",
            "короче", "так сказать", "в общем", "то есть"
        };

        public TextEnhancer(OllamaService ollama)
        {
            _ollama = ollama;
        }

        /// <summary>
        /// Основной метод: очищает текст + улучшает через LLM
        /// </summary>
        public async Task<string> EnhanceAsync(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return rawText;

            // 1. Быстрая локальная очистка
            string cleaned = QuickClean(rawText);
            if (string.IsNullOrWhiteSpace(cleaned)) return rawText;

            // 2. LLM-улучшение (только для длинных фраз)
            if (cleaned.Split(' ').Length >= 3)
            {
                try
                {
                    var response = await _ollama.AskRawAsync($@"
Исправь русский текст: убери слова-паразиты, добавь запятые и точки, сохрани смысл.
Верни ТОЛЬКО исправленный текст, без комментариев.

Текст: ""{cleaned}""", 0.2);

                    if (!string.IsNullOrWhiteSpace(response) &&
                        !response.StartsWith("Ошибка") &&
                        response.Length > 0)
                    {
                        return response.Trim();
                    }
                }
                catch
                {
                    // Fallback на локальную очистку
                }
            }

            return cleaned;
        }

        private string QuickClean(string text)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();

            foreach (var word in words)
            {
                var cleanWord = word.Trim('.', ',', '!', '?', ':', ';', '-', ' ');
                var lower = cleanWord.ToLower();

                if (!string.IsNullOrEmpty(lower) && !_fillerWords.Contains(lower))
                {
                    result.Add(word);
                }
            }

            var joined = string.Join(" ", result);

            // Базовая пунктуация: точка в конце, если её нет
            if (!string.IsNullOrWhiteSpace(joined))
            {
                joined = joined.Trim();
                var lastChar = joined[joined.Length - 1];
                if (!".?!".Contains(lastChar))
                {
                    joined += ".";
                }

                // Заглавная буква в начале
                joined = char.ToUpper(joined[0]) + joined.Substring(1);
            }

            return joined;
        }
    }
}