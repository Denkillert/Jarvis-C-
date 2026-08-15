using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        private readonly VADService _vad;
        private readonly TextEnhancer _textEnhancer;
        private readonly IntelligentCommandParser _commandParser;
        private readonly ContextDetector _contextDetector;

        private bool _isProcessing = false;
        private bool _isMicOn = false;
        private bool _setupDone = false;

        public MainWindow()
        {
            InitializeComponent();

            // === Инициализация сервисов ===
            _ollama = new OllamaService("qwen2.5:3b");
            _voice = new VoiceController();
            _vad = new VADService();
            _textEnhancer = new TextEnhancer(_ollama);
            _commandParser = new IntelligentCommandParser();
            _contextDetector = new ContextDetector();

            // === VAD события (визуальная индикация) ===
            _vad.OnSpeechStart += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusLabel.Text = "🎙 Слушаю...";
                    StatusLabel.Foreground = Brushes.Lime;
                });
            };

            _vad.OnSpeechEnd += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusLabel.Text = "⏸ Ожидание...";
                    StatusLabel.Foreground = Brushes.Gray;
                });
            };

            // === Голосовые события ===
            _voice.OnWakeWordDetected += () =>
            {
                Dispatcher.Invoke(async () =>
                {
                    AddMessage("Джарвис", "Слушаю...", Brushes.Yellow);
                    await _voice.SpeakAndWaitAsync("Да?");
                });
            };

            _voice.OnCommandRecognized += async (rawText) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    // 🔥 1. Исправляем ошибки распознавания ("ским" → "стим")
                    var corrected = RecognitionCorrector.Correct(rawText);
                    // 2. Улучшаем текст (слова-паразиты, пунктуация)
                    var enhancedText = await _textEnhancer.EnhanceAsync(corrected);
                    InputBox.Text = enhancedText;
                    await ProcessMessage();
                });
            };

            // === Привязываем VAD к VoiceController ===
            _voice.OnAudioDataAvailable += (buffer, bytesRecorded) =>
            {
                _vad.ProcessAudio(buffer, bytesRecorded);
            };

            AddMessage("Джарвис", "Система готова. Скажите 'Джарвис' для активации.", Brushes.LightGreen);

            if (!_voice.IsModelLoaded)
            {
                AddMessage("Джарвис", "⚠ Модель речи не найдена — скачиваю, секунду...", Brushes.Orange);
            }

            StatusLabel.Text = "⏸ Ожидание...";
            StatusLabel.Foreground = Brushes.Gray;

            _voice.StartListening();
            _isMicOn = true;
            MicButton.Background = Brushes.Red;

            // === Мастер первого запуска (модели + Ollama) ===
            Loaded += async (s, e) => await RunSetupAsync();
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
                _vad.Reset();
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
                string responseText = null;

                // === ШАГ 1: быстрый парсер (без LLM) ===
                var parsed = _commandParser.Parse(text);
                if (parsed != null && parsed.Confidence >= 0.85)
                {
                    ChatParagraph.Inlines.Remove(thinkingRun);

                    var jsonParams = JsonSerializer.SerializeToElement(parsed.Parameters);
                    var toolResult = await ExecuteTool(parsed.Action, jsonParams);

                    if (parsed.Action == "get_time" || parsed.Action == "get_date")
                    {
                        responseText = toolResult ?? "Не удалось получить информацию.";
                    }
                    else if (parsed.Action == "run_cmd")
                    {
                        responseText = toolResult;
                    }
                    else
                    {
                        responseText = toolResult ?? "Готово.";
                    }
                }
                else
                {
                    // === ШАГ 2: LLM с контекстом ===
                    var context = _contextDetector.GetCurrentContext();

                    AgentResponse response = await _ollama.AskAsync(text);
                    ChatParagraph.Inlines.Remove(thinkingRun);

                    // 🔥 ЗАЩИТА 1: нейросеть ответила голым именем команды ("get_time") → считаем это действием
                    if (string.IsNullOrWhiteSpace(response.action) && IsKnownAction(response.text))
                    {
                        response.action = response.text.Trim().ToLower();
                        response.text = "";
                    }

                    // 🔥 ЗАЩИТА 2: нет параметров → подставляем пустой объект, чтобы ExecuteTool не крашился
                    if (response.parameters.ValueKind != JsonValueKind.Object)
                    {
                        response.parameters = JsonSerializer.SerializeToElement(new Dictionary<string, string>());
                    }

                    // Выполняем действие, если LLM его определил
                    string toolResult = null;
                    if (!string.IsNullOrWhiteSpace(response.action))
                    {
                        toolResult = await ExecuteTool(response.action, response.parameters);
                    }

                    // Определяем, что говорить
                    if (!string.IsNullOrWhiteSpace(toolResult) &&
                        (response.action.ToLower() == "get_time" ||
                         response.action.ToLower() == "get_date" ||
                         response.action.ToLower() == "run_cmd"))
                    {
                        responseText = toolResult;
                    }
                    else if (!string.IsNullOrWhiteSpace(response.text))
                    {
                        responseText = response.text;
                    }
                    else
                    {
                        responseText = toolResult;
                    }
                }

                // 🔥 ЗАЩИТА 3: время и дату берём ТОЛЬКО из системы — нейросеть их не знает
                var lowerInput = text.ToLower();
                if (Regex.IsMatch(lowerInput, @"(?:сколько|который)\s+время"))
                {
                    responseText = SystemController.GetTime();
                }
                else if (Regex.IsMatch(lowerInput, @"(?:какая|какой)\s+(?:сегодня\s+)?дата"))
                {
                    responseText = SystemController.GetDate();
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
                await Task.CompletedTask;

                // 🔥 ЗАЩИТА: если нейросеть не вернула "parameters", JsonElement пустой (Undefined)
                // и TryGetProperty бросает "Operation is not valid due to the current state of the object"
                if (parameters.ValueKind != JsonValueKind.Object)
                {
                    parameters = JsonSerializer.SerializeToElement(new Dictionary<string, string>());
                }

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
                    "get_time" => SystemController.GetTime(),
                    "get_date" => SystemController.GetDate(),
                    _ => null
                };

                // Системное сообщение — только для действий с приложениями
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
        }

        // ===== Мастер первого запуска =====

        private async Task RunSetupAsync()
        {
            if (_setupDone) return;
            _setupDone = true;

            try
            {
                // 1. Речь — качаем в папку проекта
                if (!SetupService.IsVoskReady())
                {
                    await SetupService.EnsureVoskAsync(
                        msg => Dispatcher.Invoke(() => StatusLabel.Text = "⚙ " + msg),
                        pct => Dispatcher.Invoke(() => StatusLabel.Text = $"⚙ Модель речи: {pct}%"));

                    if (_voice.TryLoadModel(SetupService.VoskModelPath) && _isMicOn)
                        _voice.StartListening();
                }

                // 2. Мозг — в фоне, Ollama стартует сама
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SetupService.EnsureOllamaAsync(
                            msg => Dispatcher.Invoke(() => StatusLabel.Text = "⚙ " + msg));
                    }
                    finally
                    {
                        Dispatcher.Invoke(() => StatusLabel.Text = "⏸ Ожидание...");
                    }
                });

                AddMessage("Джарвис", "Все компоненты готовы. Скажите 'Джарвис'.", Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                AddMessage("Джарвис", $"Ошибка установки: {ex.Message}", Brushes.Orange);
            }
        }

        // ===== Предупреждение при закрытии: Ollama остаётся в памяти =====

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (SetupService.OllamaStartedByUs)
            {
                var res = MessageBox.Show(
                    "Джарвис закрывается.\n\n" +
                    "Нейросеть Ollama всё ещё работает в фоне и занимает оперативную память (~2 ГБ).\n\n" +
                    "ДА  — выйти из Ollama вместе с Джарвисом\n" +
                    "НЕТ — оставить Ollama работать\n" +
                    "ОТМЕНА — не закрывать Джарвис",
                    "Закрытие",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (res == MessageBoxResult.Yes)
                {
                    SetupService.StopOllama();
                }
            }

            base.OnClosing(e);
        }

        // ===== Вспомогательные =====

        private static readonly HashSet<string> _knownActions = new()
        {
            "open_app", "close_app", "focus_app", "open_url", "run_cmd",
            "shutdown", "restart", "lock_screen",
            "volume_up", "volume_down", "volume_mute",
            "get_time", "get_date"
        };

        private bool IsKnownAction(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return _knownActions.Contains(text.Trim().ToLower());
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