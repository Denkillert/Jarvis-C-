using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Jarvis
{
    public partial class MainWindow : Window
    {
        private readonly OllamaService _ollama;
        private readonly CommandManager _commandManager;
        private bool _isProcessing = false;

        public MainWindow()
        {
            InitializeComponent();

            _ollama = new OllamaService("qwen2.5:3b");
            _commandManager = new CommandManager();

            AddMessage("Джарвис", "Система готова. Жду ваших указаний.", Brushes.LightGreen);

            SendButton.Click += SendButton_Click;
            InputBox.KeyDown += InputBox_KeyDown;

            InputBox.Focus();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isProcessing)
            {
                SendMessage();
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isProcessing)
            {
                SendMessage();
            }
        }

        private async void SendMessage()
        {
            string text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _isProcessing = true;
            SendButton.IsEnabled = false;
            InputBox.IsEnabled = false;

            AddMessage("Вы", text, Brushes.LightBlue);
            InputBox.Text = "";

            var thinkingRun = new Run("Джарвис думает...\n");
            ChatHistory.Inlines.Add(thinkingRun);
            ScrollToBottom();

            try
            {
                // Теперь получаем AgentResponse вместо строки!
                AgentResponse response = await _ollama.AskAsync(text);

                ChatHistory.Inlines.Remove(thinkingRun);

                // 1. Показываем текст
                if (!string.IsNullOrWhiteSpace(response.text))
                {
                    AddMessage("Джарвис", response.text, Brushes.LightGreen);
                }

                // 2. Если есть команда — выполняем
                if (!string.IsNullOrWhiteSpace(response.action))
                {
                    await ExecuteCommand(response.action, response.parameters);
                }
            }
            catch (Exception ex)
            {
                ChatHistory.Inlines.Remove(thinkingRun);
                AddMessage("Джарвис", $"Ошибка: {ex.Message}", Brushes.Red);
            }

            _isProcessing = false;
            SendButton.IsEnabled = true;
            InputBox.IsEnabled = true;
            InputBox.Focus();
        }

        private async Task ExecuteCommand(string action, JsonElement parameters)
        {
            try
            {
                string result = action.ToLower() switch
                {
                    "open_app" => SystemController.OpenApp(_commandManager.GetProcessName(parameters.GetProperty("name").GetString() ?? "")),

                    // Мягкое закрытие (сворачивание в трей для Discord/Steam)
                    "close_app" => SystemController.CloseApp(_commandManager.GetProcessName(parameters.GetProperty("name").GetString() ?? ""), forceKill: false),

                    // Полное закрытие (убивает процесс намертво)
                    "force_close_app" => SystemController.CloseApp(_commandManager.GetProcessName(parameters.GetProperty("name").GetString() ?? ""), forceKill: true),

                    "open_url" => SystemController.OpenUrl(parameters.GetProperty("url").GetString() ?? ""),
                    "open_explorer" => SystemController.OpenExplorer(parameters.TryGetProperty("path", out var p) ? p.GetString() ?? "" : ""),
                    "run_cmd" => SystemController.RunCmd(parameters.GetProperty("command").GetString() ?? ""),
                    "lock_screen" => SystemController.LockScreen(),
                    "shutdown" => SystemController.Shutdown(),
                    "cancel_shutdown" => SystemController.CancelShutdown(),
                    "restart" => SystemController.Restart(),
                    "open_calculator" => SystemController.OpenApp(_commandManager.GetProcessName("калькулятор")),
                    "open_notepad" => SystemController.OpenApp(_commandManager.GetProcessName("блокнот")),
                    "open_task_manager" => SystemController.OpenApp(_commandManager.GetProcessName("диспетчер задач")),
                    "open_cmd" => SystemController.OpenApp("cmd"),
                    "get_system_info" => SystemController.GetSystemInfo(),
                    _ => $"Неизвестная команда: {action}"
                };

                if (!string.IsNullOrWhiteSpace(result))
                {
                    AddMessage("Джарвис", $"[Система]: {result}", Brushes.Gray);
                }
            }
            catch (Exception ex)
            {
                AddMessage("Джарвис", $"[Ошибка выполнения]: {ex.Message}", Brushes.Red);
            }

            await Task.CompletedTask;
        }

        private void AddMessage(string sender, string text, Brush color)
        {
            var senderRun = new Run($"{sender}: ") { Foreground = color, FontWeight = FontWeights.Bold };
            var textRun = new Run($"{text}\n\n");

            ChatHistory.Inlines.Add(senderRun);
            ChatHistory.Inlines.Add(textRun);
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(ChatHistory);
            scrollViewer?.ScrollToEnd();
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var found = FindVisualChild<T>(child);
                if (found != null)
                    return found;
            }
            return null;
        }
    }

    // Класс для десериализации JSON команды (должен быть public или internal)
    public class CommandData
    {
        public string action { get; set; } = "";
        public JsonElement parameters { get; set; }
    }
}