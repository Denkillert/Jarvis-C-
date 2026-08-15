using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jarvis
{
    public class IntelligentCommandParser
    {
        private static readonly HashSet<string> StopWords = new()
        {
            "мне", "из", "для", "пожалуйста", "бы", "тут", "это", "кажется", "например"
        };

        public ParsedCommand Parse(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            var lower = command.ToLower().Trim().TrimEnd('.', '!', '?');

            if (Regex.IsMatch(lower, @"(?:сколько|который)\s+время"))
                return new ParsedCommand { Action = "get_time", Confidence = 0.95 };

            if (Regex.IsMatch(lower, @"(?:какая|какой)\s+(?:сегодня\s+)?дата"))
                return new ParsedCommand { Action = "get_date", Confidence = 0.95 };

            if (lower.Contains("youtube") || lower.Contains("ютуб"))
            {
                var query = ExtractAfterKeywords(lower,
                    new[] { "найди", "включи", "посмотреть", "посмотри", "открой" });
                if (!string.IsNullOrEmpty(query))
                {
                    return new ParsedCommand
                    {
                        Action = "open_url",
                        Parameters = { ["url"] = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}" },
                        Confidence = 0.9
                    };
                }
            }

            if (Regex.IsMatch(lower, @"(?:сделай\s+)?(?:по)?громче"))
                return new ParsedCommand { Action = "volume_up", Confidence = 0.9 };

            if (Regex.IsMatch(lower, @"(?:сделай\s+)?(?:по)?тише"))
                return new ParsedCommand { Action = "volume_down", Confidence = 0.9 };

            if (Regex.IsMatch(lower, @"без\s+звука|выключи\s+звук"))
                return new ParsedCommand { Action = "volume_mute", Confidence = 0.9 };

            if (Regex.IsMatch(lower, @"(?:выключ|заверш).*(?:работ|компьютер|пк)"))
                return new ParsedCommand { Action = "shutdown", Confidence = 0.9 };

            if (Regex.IsMatch(lower, @"перезагруз|рестарт"))
                return new ParsedCommand { Action = "restart", Confidence = 0.9 };

            if (Regex.IsMatch(lower, @"заблок.*(?:экран|компьютер)"))
                return new ParsedCommand { Action = "lock_screen", Confidence = 0.9 };

            // === Открыть приложение ===
            var openMatch = Regex.Match(lower, @"(?:открой|запусти|включи|открыть)\s+(.+?)$");
            if (openMatch.Success)
            {
                var target = CleanTarget(openMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(target))
                {
                    var canonical = AppResolver.ToCanonical(target);
                    if (canonical != null)
                    {
                        return new ParsedCommand
                        {
                            Action = "open_app",
                            Parameters = { ["name"] = canonical },
                            Confidence = 0.9
                        };
                    }

                    if (AppResolver.Resolve(target) != null)
                    {
                        return new ParsedCommand
                        {
                            Action = "open_app",
                            Parameters = { ["name"] = target },
                            Confidence = 0.88
                        };
                    }
                }
            }

            // === Закрыть приложение ===
            var closeMatch = Regex.Match(lower, @"(?:закрой|выключи|закрыть)\s+(.+?)$");
            if (closeMatch.Success)
            {
                var target = CleanTarget(closeMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(target))
                {
                    var canonical = AppResolver.ToCanonical(target);
                    if (canonical != null)
                    {
                        return new ParsedCommand
                        {
                            Action = "close_app",
                            Parameters = { ["name"] = canonical },
                            Confidence = 0.9
                        };
                    }

                    if (AppResolver.Resolve(target) != null)
                    {
                        return new ParsedCommand
                        {
                            Action = "close_app",
                            Parameters = { ["name"] = target },
                            Confidence = 0.88
                        };
                    }
                }
            }

            return null;
        }

        private string CleanTarget(string raw)
        {
            var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Where(w => !StopWords.Contains(w))).Trim();
        }

        private string ExtractAfterKeywords(string text, string[] keywords)
        {
            foreach (var kw in keywords)
            {
                var pattern = $@"{Regex.Escape(kw)}\s+(.+?)(?:\s+(?:на|в)\s+(?:youtube|ютуб))?$";
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var result = match.Groups[1].Value.Trim();
                    result = Regex.Replace(result, @"\s+(?:youtube|ютуб|ютубе)$", "", RegexOptions.IgnoreCase);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }
            return null;
        }
    }

    public class ParsedCommand
    {
        public string Action { get; set; } = "";
        public Dictionary<string, string> Parameters { get; set; } = new();
        public double Confidence { get; set; }
    }
}