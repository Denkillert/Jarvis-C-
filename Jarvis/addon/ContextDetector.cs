using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jarvis
{
    /// <summary>
    /// Определяет контекст: какое приложение сейчас активно
    /// Полезно для умных ответов (например, "напиши письмо" работает по-разному в Outlook и Discord)
    /// </summary>
    public class ContextDetector
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public AppContext GetCurrentContext()
        {
            var context = new AppContext();

            try
            {
                var hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    var title = new System.Text.StringBuilder(512);
                    GetWindowText(hWnd, title, 512);
                    context.WindowTitle = title.ToString();

                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid > 0)
                    {
                        var process = Process.GetProcessById((int)pid);
                        context.AppName = process.ProcessName;
                        context.Type = DetectAppType(process.ProcessName);
                    }
                }
            }
            catch (Exception ex)
            {
                context.Error = ex.Message;
            }

            return context;
        }

        private AppType DetectAppType(string processName)
        {
            var lower = processName.ToLower();

            if (lower.Contains("chrome") || lower.Contains("firefox") ||
                lower.Contains("msedge") || lower.Contains("brave"))
                return AppType.Browser;

            if (lower.Contains("notepad") || lower.Contains("word") ||
                lower.Contains("code") || lower.Contains("sublime"))
                return AppType.TextEditor;

            if (lower.Contains("explorer"))
                return AppType.FileManager;

            if (lower.Contains("cmd") || lower.Contains("powershell") ||
                lower.Contains("terminal"))
                return AppType.Terminal;

            if (lower.Contains("outlook") || lower.Contains("thunderbird"))
                return AppType.Email;

            return AppType.Other;
        }
    }

    public class AppContext
    {
        public string AppName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public AppType Type { get; set; } = AppType.Other;
        public string Error { get; set; } = "";

        public string ToDescription()
        {
            if (!string.IsNullOrEmpty(Error)) return $"Ошибка: {Error}";
            return $"Активно: {AppName} ({Type}) — {WindowTitle}";
        }
    }

    public enum AppType
    {
        Other,
        Browser,
        TextEditor,
        FileManager,
        Terminal,
        Email
    }
}