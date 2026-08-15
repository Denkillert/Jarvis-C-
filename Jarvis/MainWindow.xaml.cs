using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Jarvis
{
    public partial class MainWindow : Window
    {
        private readonly OllamaService _ollama;
        private readonly VoiceController _voice;
        private bool _isProcessing = false;
        private bool _isMicOn = false;

        public MainWindow()
        {
            InitializeComponent();

            _ollama = new OllamaService("qwen2.5:3b");
            _voice = new VoiceController();

            // 1. Когда услышали "Джарвис"
            _voice.OnWakeWordDetected += () =>
            {
                Dispatcher.Invoke(async () =>
                {
                    AddMessage("Джарвис", "Слушаю...", Brushes.Yellow);
                    // Говорим "Да?" и ЖДЕМ окончания, чтобы не начать слушать команду поверх своего голоса
                    await _voice.SpeakAndWaitAsync("Да?");
                });
            };

            // 2. Когда услышали команду
            _voice.OnCommandRecognized += async (text) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    InputBox.Text = text;
                    await ProcessMessage();
                });
            };

            AddMessage("Джарвис", "Система готова. Скажите 'Джарвис' для активации.", Brushes.LightGreen);

            // Включаем микрофон при старте (он сразу в режиме "Жду слово Джарвис")
            _voice.StartListening();
            _isMicOn = true;
            MicButton.Background = Brushes.Red;
        }

        private void MicButton_Click(object sender, RoutedEventArgs e)
        {
            _isMicOn = !_isMicOn;
            if (_isMicOn)
            {
                _voice.StartListening();
                MicButton.Background = Brushes.Red;
            }
            else
            {
                _voice.StopListening();
                MicButton.Background = (Brush)new BrushConverter().ConvertFrom("#3E3E42");
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isProcessing) ProcessMessage();
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isProcessing) await ProcessMessage();
        }

        private async Task ProcessMessage()
        {
            string text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _isProcessing = true;
            SendButton.IsEnabled = false;
            InputBox.IsEnabled = false;

            // Выключаем микрофон, пока думаем и говорим
            _voice.StopListening();

            AddMessage("Вы", text, Brushes.LightBlue);
            InputBox.Text = "";

            var thinkingRun = new Run("Джарвис думает...\n");
            ChatParagraph.Inlines.Add(thinkingRun);
            ChatHistory.ScrollToEnd();

            try
            {
                AgentResponse response = await _ollama.AskAsync(text);
                ChatParagraph.Inlines.Remove(thinkingRun);

                // Выполняем действие (если есть)
                if (!string.IsNullOrWhiteSpace(response.action))
                {
                    await ExecuteTool(response.action, response.parameters);
                }

                // Говорим ответ и ЖДЕМ его окончания
                if (!string.IsNullOrWhiteSpace(response.text))
                {
                    AddMessage("Джарвис", response.text, Brushes.LightGreen);
                    await _voice.SpeakAndWaitAsync(response.text);
                }

                // КЛЮЧЕВОЙ МОМЕНТ: После того как он замолчал, даем 5 секунд на команду
                if (_isMicOn)
                {
                    _voice.StartListeningForCommandsAfterResponse();
                }
            }
            catch (Exception ex)
            {
                ChatParagraph.Inlines.Remove(thinkingRun);
                AddMessage("Джарвис", $"Ошибка: {ex.Message}", Brushes.Red);

                if (_isMicOn) _voice.StartListening();
            }

            _isProcessing = false;
            SendButton.IsEnabled = true;
            InputBox.IsEnabled = true;
            InputBox.Focus();
        }

        private async Task ExecuteTool(string action, JsonElement parameters)
        {
            try
            {
                string appName = parameters.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                string result = action.ToLower() switch
                {
                    "open_app" => SystemController.OpenApp(appName),
                    "close_app" => SystemController.CloseApp(appName, forceKill: false),
                    "open_url" => SystemController.OpenUrl(parameters.GetProperty("url").GetString() ?? ""),
                    _ => $"Неизвестный инструмент: {action}"
                };

                if (!string.IsNullOrWhiteSpace(result))
                {
                    AddMessage("Джарвис", $"[Система]: {result}", Brushes.Gray);
                }
            }
            catch (Exception ex)
            {
                AddMessage("Джарвис", $"[Ошибка]: {ex.Message}", Brushes.Red);
            }
            await Task.CompletedTask;
        }

        private void AddMessage(string sender, string text, Brush color)
        {
            var senderRun = new Run($"{sender}: ") { Foreground = color, FontWeight = FontWeights.Bold };
            var textRun = new Run($"{text}\n\n");
            ChatParagraph.Inlines.Add(senderRun);
            ChatParagraph.Inlines.Add(textRun);
            ChatHistory.ScrollToEnd();
        }
    }
}