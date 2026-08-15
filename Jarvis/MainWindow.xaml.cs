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

            _voice.OnWakeWordDetected += () =>
            {
                Dispatcher.Invoke(async () =>
                {
                    AddMessage("Джарвис", "Слушаю...", Brushes.Yellow);
                    await _voice.SpeakAndWaitAsync("Да?");
                });
            };

            _voice.OnCommandRecognized += async (text) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    InputBox.Text = text;
                    await ProcessMessage();
                });
            };

            AddMessage("Джарвис", "Система готова. Скажите 'Джарвис' для активации.", Brushes.LightGreen);

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

                // 🔥 ВЫПОЛНЯЕМ ДЕЙСТВИЕ И ПОЛУЧАЕМ РЕЗУЛЬТАТ
                string toolResult = null;
                if (!string.IsNullOrWhiteSpace(response.action))
                {
                    toolResult = await ExecuteTool(response.action, response.parameters);
                }

                // 🔥 ОПРЕДЕЛЯЕМ ЧТО ГОВОРИТЬ:
                string responseText;

                // Если инструмент вернул результат (время, дата, команды) — используем его
                if (!string.IsNullOrWhiteSpace(toolResult) &&
                    (response.action.ToLower() == "get_time" ||
                     response.action.ToLower() == "get_date" ||
                     response.action.ToLower() == "run_cmd"))
                {
                    responseText = toolResult;
                }
                // Иначе используем текст от нейросети
                else if (!string.IsNullOrWhiteSpace(response.text))
                {
                    responseText = response.text;
                }
                // Или результат инструмента
                else
                {
                    responseText = toolResult;
                }

                // Говорим ответ
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    AddMessage("Джарвис", responseText, Brushes.LightGreen);
                    await _voice.SpeakAndWaitAsync(responseText);
                }

                // Возвращаемся к прослушиванию
                if (_isMicOn)
                {
                    _voice.StartListeningForCommands();
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

        private async Task<string> ExecuteTool(string action, JsonElement parameters)
        {
            try
            {
                string appName = parameters.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                string url = parameters.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                string command = parameters.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() ?? "" : "";

                string result = action.ToLower() switch
                {
                    "open_app" => SystemController.OpenApp(appName),
                    "close_app" => SystemController.CloseApp(appName),
                    "focus_app" => SystemController.FocusApp(appName),
                    "open_url" => SystemController.OpenUrl(url),
                    "run_cmd" => SystemController.RunCmd(command),
                    "shutdown" => SystemController.Shutdown(),
                    "restart" => SystemController.Restart(),
                    "lock_screen" => SystemController.LockScreen(),
                    "volume_up" => SystemController.VolumeUp(),
                    "volume_down" => SystemController.VolumeDown(),
                    "volume_mute" => SystemController.VolumeMute(),
                    "get_time" => DateTime.Now.ToString("HH:mm"),
                    "get_date" => DateTime.Now.ToString("dd MMMM yyyy"),
                    _ => null
                };

                // Показываем системное сообщение только для действий с приложениями
                if (!string.IsNullOrWhiteSpace(result) &&
                    action.ToLower() != "get_time" &&
                    action.ToLower() != "get_date")
                {
                    AddMessage("Джарвис", $"[Система]: {result}", Brushes.Gray);
                }

                return result;
            }
            catch (Exception ex)
            {
                AddMessage("Джарвис", $"[Ошибка]: {ex.Message}", Brushes.Red);
                return null;
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