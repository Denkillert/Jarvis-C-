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
        private readonly VoiceController _voice;

        private bool _isProcessing = false;
        private bool _isMicOn = false;

        public MainWindow()
        {
            InitializeComponent();

            _ollama = new OllamaService("qwen2.5:3b");
            _commandManager = new CommandManager();
            _voice = new VoiceController();

            // Подписка на WAKE WORD "Джарвис"
            _voice.OnWakeWordDetected += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage("", "🎤 Джарвис слушает...", Brushes.Yellow);
                    _voice.Speak("Да?");
                });
            };

            // Подписка на КОМАНДЫ
            _voice.OnCommandRecognized += (commandText) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    InputBox.Text = commandText;
                    await SendMessage();
                });
            };

            AddMessage("Джарвис", "Система готова. Скажите 'Джарвис' для активации.", Brushes.LightGreen);

            SendButton.Click += SendButton_Click;
            InputBox.KeyDown += InputBox_KeyDown;
            MicButton.Click += MicButton_Click;

            // Автозапуск микрофона
            _voice.StartListening();
            _isMicOn = true;
            MicButton.Background = Brushes.Red;
            MicButton.Content = "🛑";

            InputBox.Focus();
        }

        private void MicButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMicOn)
            {
                _voice.StartListening();
                MicButton.Background = Brushes.Red;
                MicButton.Content = "🛑";
                _isMicOn = true;
            }
            else
            {
                _voice.StopListening();
                MicButton.Background = Brushes.Transparent;
                MicButton.Content = "";
                _isMicOn = false;
            }
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

        private async Task SendMessage()
        {
            string text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _isProcessing = true;
            SendButton.IsEnabled = false;
            InputBox.IsEnabled = false;

            // 🔴 ВЫКЛЮЧАЕМ МИКРОФОН пока думаем и говорим
            _voice.StopListening();

            AddMessage("Вы", text, Brushes.LightBlue);
            InputBox.Text = "";

            var thinkingRun = new Run("Джарвис думает...\n");
            ChatHistory.Inlines.Add(thinkingRun);
            ScrollToBottom();

            try
            {
                AgentResponse response = await _ollama.AskAsync(text);

                ChatHistory.Inlines.Remove(thinkingRun);

                if (!string.IsNullOrWhiteSpace(response.text))
                {
                    AddMessage("Джарвис", response.text, Brushes.LightGreen);

                    // Ждём пока РЕАЛЬНО закончит говорить (будь то 2 секунды или 2 минуты)
                    await _voice.SpeakAndWaitAsync(response.text);

                    // 🎤 СРАЗУ после окончания речи включаем микрофон в режиме команд
                    if (_isMicOn)
                    {
                        _voice.StartListeningForCommands();
                    }
                }

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
                    "close_app" => SystemController.CloseApp(_commandManager.GetProcessName(parameters.GetProperty("name").GetString() ?? ""), forceKill: false),
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
}