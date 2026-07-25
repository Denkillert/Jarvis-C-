using NAudio.Wave;
using System;
using System.Globalization;
using System.IO;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using Vosk;

namespace Jarvis
{
    public class VoiceController : IDisposable
    {
        private Model _model;
        private VoskRecognizer _recognizer;
        private SpeechSynthesizer _synthesizer;
        private WaveInEvent _waveIn;

        private bool _isListening = false;
        private bool _awaitingCommand = false;  // Ждём команду после "Джарвис"
        private System.Timers.Timer _responseTimer;  // Таймер после ответа Джарвиса
        private bool _isSpeaking = false;  // Джарвис сейчас говорит

        public event Action<string> OnCommandRecognized;  // Для команд
        public event Action OnWakeWordDetected;  // Когда услышал "Джарвис"

        public VoiceController()
        {
            // СИНТЕЗ
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.Rate = 0;
            _synthesizer.Volume = 100;

            try
            {
                _synthesizer.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult, 0, new CultureInfo("ru-RU"));
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("[Voice] Русский голос не найден");
            }

            // Подписка на окончание речи
            _synthesizer.SpeakCompleted += (s, e) =>
            {
                _isSpeaking = false;
                System.Diagnostics.Debug.WriteLine("[Voice] Джарвис закончил говорить");
            };

            // ТАЙМЕР - ждём 2 секунды после ответа
            _responseTimer = new System.Timers.Timer(5000);
            _responseTimer.Elapsed += OnResponseTimerElapsed;
            _responseTimer.AutoReset = false;

            // РАСПОЗНАВАНИЕ
            try
            {
                string modelPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Models", "ru", "vosk-model-small-ru-0.22");

                System.Diagnostics.Debug.WriteLine($"[Vosk] Путь: {modelPath}");

                if (Directory.Exists(modelPath))
                {
                    _model = new Model(modelPath);
                    _recognizer = new VoskRecognizer(_model, 16000.0f);
                    _recognizer.SetMaxAlternatives(0);
                    _recognizer.SetWords(false);
                    System.Diagnostics.Debug.WriteLine("[Vosk] OK");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Vosk] НЕ НАЙДЕНА: {modelPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Vosk] Ошибка: {ex.Message}");
            }
        }

        public void StartListening(bool wakeWordMode = true)
        {
            if (_recognizer == null)
            {
                System.Diagnostics.Debug.WriteLine("[Voice] Vosk не готов");
                return;
            }

            if (!_isListening)
            {
                try
                {
                    _waveIn = new WaveInEvent();
                    _waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
                    _waveIn.DataAvailable += OnDataAvailable;
                    _waveIn.StartRecording();
                    _isListening = true;

                    // НЕ СБРАСЫВАЕМ если не указано явно
                    if (wakeWordMode)
                    {
                        _awaitingCommand = false;
                        System.Diagnostics.Debug.WriteLine("[Voice] 🔴 Слушаю слово 'Джарвис'...");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Voice] Ошибка: {ex.Message}");
                }
            }
        }

        public void StartListeningForCommands()
        {
            _awaitingCommand = true;
            _responseTimer.Start();

            // Запускаем микрофон НЕ сбрасывая режим
            if (!_isListening)
            {
                try
                {
                    _waveIn = new WaveInEvent();
                    _waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
                    _waveIn.DataAvailable += OnDataAvailable;
                    _waveIn.StartRecording();
                    _isListening = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Voice] Ошибка: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine("[Voice] 🎤 Слушаю команду (у тебя есть 5 сек)...");
        }

        public void StopListening()
        {
            if (_isListening)
            {
                _waveIn?.StopRecording();
                _isListening = false;
                _awaitingCommand = false;
                _responseTimer?.Stop();
                System.Diagnostics.Debug.WriteLine("[Voice] ⏹ Остановил");
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_recognizer == null || e.BytesRecorded == 0) return;

            bool result = _recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded);

            if (result)
            {
                // Финальный результат
                string jsonResult = _recognizer.Result();
                ProcessRecognition(jsonResult);
            }
            else
            {
                // Частичный результат (пока говорят)
                string partial = _recognizer.PartialResult();
                ProcessPartialRecognition(partial);
            }
        }

        private void ProcessPartialRecognition(string partialJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(partialJson);
                if (doc.RootElement.TryGetProperty("partial", out var partialElement))
                {
                    string partialText = partialElement.GetString();
                    if (!string.IsNullOrWhiteSpace(partialText))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Voice] Частично: {partialText}");
                    }
                }
            }
            catch { }
        }

        private void ProcessRecognition(string jsonResult)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonResult);
                if (doc.RootElement.TryGetProperty("text", out var textElement))
                {
                    string text = textElement.GetString();

                    if (string.IsNullOrWhiteSpace(text)) return;

                    // 🎯 ГЛАВНОЕ: Если Джарвис сейчас говорит — ИГНОРИРУЕМ всё
                    if (_isSpeaking)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Voice] Игнорирую (Джарвис говорит): {text}");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"[Voice] Распознано: '{text}' (режим: {(_awaitingCommand ? "КОМАНДА" : "WAKE WORD")})");

                    if (_awaitingCommand)
                    {
                        // Режим КОМАНДЫ
                        if (text.ToLower().Contains("стоп") || text.ToLower().Contains("хватит"))
                        {
                            System.Diagnostics.Debug.WriteLine("[Voice] Услышал 'стоп' - возвращаюсь к ожиданию");
                            _awaitingCommand = false;
                            _responseTimer.Stop();
                        }
                        else
                        {
                            OnCommandRecognized?.Invoke(text);
                        }
                    }
                    else
                    {
                        // Режим WAKE WORD
                        if (text.ToLower().Contains("джарвис") || text.ToLower().Contains("jarvis"))
                        {
                            System.Diagnostics.Debug.WriteLine("[Voice] ✅ Услышал 'Джарвис'!");
                            OnWakeWordDetected?.Invoke();
                            _awaitingCommand = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Voice] Ошибка парсинга: {ex.Message}");
            }
        }

        // Вызывается из MainWindow когда Джарвис начал говорить
        public void OnJarvisStartedSpeaking()
        {
            _isSpeaking = true;
            _responseTimer.Stop();
        }

        // Вызывается из MainWindow когда Джарвис закончил говорить
        public void OnJarvisFinishedSpeaking()
        {
            _isSpeaking = false;
            _responseTimer.Start();  // Запускаем таймер на 2 секунды
            System.Diagnostics.Debug.WriteLine("[Voice] ⏱ Таймер запущен (2 сек)...");
        }

        private void OnResponseTimerElapsed(object sender, ElapsedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[Voice] ⏱ Таймер истёк - жду 'Джарвис'");
            _awaitingCommand = false;
        }

        public void Speak(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = text.Replace("*", "").Replace("#", "").Replace("[", "").Replace("]", "");
                _synthesizer.SpeakAsync(text);
            }
        }
        public async Task SpeakAndWaitAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // Очищаем от спецсимволов
            text = text.Replace("*", "").Replace("#", "").Replace("[", "").Replace("]", "");

            // Создаем источник задачи, который завершится, когда речь закончится
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();

            // Временный обработчик события окончания речи
            System.EventHandler<System.Speech.Synthesis.SpeakCompletedEventArgs> handler = null;
            handler = (sender, e) =>
            {
                _synthesizer.SpeakCompleted -= handler; // Отписываемся, чтобы не было утечек памяти
                tcs.SetResult(true);                    // Сообщаем, что речь закончена
            };

            _synthesizer.SpeakCompleted += handler;
            _synthesizer.SpeakAsync(text);              // Начинаем говорить

            // Ждем здесь ровно столько, сколько нужно Джарвису, чтобы договорить
            await tcs.Task;

            System.Diagnostics.Debug.WriteLine("[Voice] Джарвис закончил говорить.");
        }

        public void ResetToWakeWord()
        {
            _awaitingCommand = false;
            _responseTimer.Stop();
            System.Diagnostics.Debug.WriteLine("[Voice] Сброс к режиму WAKE WORD");
        }

        public void Dispose()
        {
            StopListening();
            _responseTimer?.Dispose();
            _waveIn?.Dispose();
            _recognizer?.Dispose();
            _model?.Dispose();
            _synthesizer?.Dispose();
        }
    }
}