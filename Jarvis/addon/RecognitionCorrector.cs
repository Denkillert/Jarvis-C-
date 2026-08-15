using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarvis
{
    /// <summary>
    /// Исправляет типичные ошибки распознавания ДО обработки.
    /// "открой ским" → "открой стим", "джарвес" → "джарвис".
    /// Не требует моделей и места на диске.
    /// </summary>
    public static class RecognitionCorrector
    {
        private static readonly HashSet<string> Vocabulary = new()
        {
            // Глаголы команд
            "открой", "открыть", "запусти", "включи", "закрой", "закрыть", "выключи",
            "заблокируй", "перезагрузи", "сделай", "найди", "посмотреть", "посмотри",

            // Приложения (русские названия)
            "стим", "телеграм", "телега", "дискорд", "хром", "гугл", "браузер",
            "блокнот", "калькулятор", "проводник", "вотсап", "скайп", "обс",
            "ворд", "эксель", "аутлук", "спотифай", "тимс",

            // Служебные слова
            "громче", "тише", "время", "дата", "звук", "экран", "компьютер",
            "джарвис", "стоп", "хватит", "отмена", "ютуб", "сколько", "который", "какая"
        };

        public static string Correct(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var norm = new string(words[i].ToLower().Where(char.IsLetterOrDigit).ToArray());
                if (norm.Length < 3) continue;
                if (Vocabulary.Contains(norm)) continue; // слово уже правильное

                string best = null;
                int bestD = int.MaxValue;
                foreach (var v in Vocabulary)
                {
                    int allowed = v.Length <= 5 ? 1 : 2;
                    if (Math.Abs(v.Length - norm.Length) > allowed) continue;
                    int d = Levenshtein(norm, v);
                    if (d <= allowed && d < bestD)
                    {
                        bestD = d;
                        best = v;
                    }
                }
                if (best != null) words[i] = best;
            }
            return string.Join(" ", words);
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
    }
}