using System;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using NAudio.Wave;
using Vosk;

namespace Jarvis
{
    public class VoiceController : IDisposable
    {
        private Model _model;
        private VoskRecognizer _recognizer;
        private SpeechSynthesizer _synthesizer;
        private WaveInEvent _waveIn;
        private System.Timers.Timer _postResponseTimer;

        private bool _isListening = false;
        private bool _isSpeaking = false;

        private bool _isWaitingForWakeWord = true;
        private bool _isWaitingForCommand = false;

        public event Action OnWakeWordDetected;
        public event Action<string> OnCommandRecognized;

        public VoiceController()
        {
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.Rate = 0;
            _synthesizer.Volume = 100;
            try
            {
                _synthesizer.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult, 0, new CultureInfo("ru-RU"));
            }
            catch { }

            _postResponseTimer = new System.Timers.Timer(5000);
            _postResponseTimer.Elapsed += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] ⏱ Таймер истек. Возврат в режим 'Джарвис'.");
                ResetToWakeWordMode();
            };
            _postResponseTimer.AutoReset = false;

            try
            {
                string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "ru", "vosk-model-small-ru-0.22");

                if (Directory.Exists(modelPath))
                {
                    _model = new Model(modelPath);

                    // ⚠️ ВРЕМЕННО УБРАЛИ ГРАММАТИКУ для проверки базовой работы
                    _recognizer = new VoskRecognizer(_model, 16000.0f);
                    _recognizer.SetMaxAlternatives(0);
                    _recognizer.SetWords(true); // Включаем слова для лучшего дебага
                    System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] ✅ Модель загружена БЕЗ грамматики (базовый режим).");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] ❌ Модель не найдена: {modelPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] ❌ Ошибка Vosk: {ex.Message}");
            }
        }

        public void StartListening()
        {
            if (_recognizer != null && !_isListening)
            {
                try
                {
                    _waveIn = new WaveInEvent();
                    _waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
                    _waveIn.DataAvailable += OnDataAvailable;

                    if (WaveInEvent.DeviceCount > 0)
                    {
                        var caps = WaveInEvent.GetCapabilities(0);
                        System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] 🎤 УСТРОЙСТВО: {caps.ProductName}");
                    }

                    _waveIn.StartRecording();
                    _isListening = true;
                    System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] 🎤 Запись начата.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] ❌ Ошибка запуска: {ex.Message}");
                }
            }
        }

        public void StopListening()
        {
            if (_isListening && _waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
                _isListening = false;
                _postResponseTimer.Stop();
                System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] ⏹ Запись остановлена.");
            }
        }

        public void StartListeningForCommandsAfterResponse()
        {
            _isWaitingForCommand = true;
            _isWaitingForWakeWord = false;
            _postResponseTimer.Start();

            if (!_isListening) StartListening();
            System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] 🟢 Режим: ЖДУ КОМАНДУ (5 сек).");
        }

        private void ResetToWakeWordMode()
        {
            _postResponseTimer.Stop();
            _isWaitingForWakeWord = true;
            _isWaitingForCommand = false;
            System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] 🔴 Режим: ЖДУ СЛОВО 'ДЖАРВИС'.");
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_recognizer == null || e.BytesRecorded == 0) return;
            if (_isSpeaking) return;

            try
            {
                // 🔥 ГЛАВНЫЙ ДЕБАГ: что возвращает Vosk при обработке звука
                bool isFinal = _recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded);

                if (isFinal)
                {
                    string jsonResult = _recognizer.Result();
                    System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] 📦 ФИНАЛЬНЫЙ JSON: {jsonResult}");
                    ProcessRecognition(jsonResult);
                }
                else
                {
                    // Показываем "сырой" частичный результат, чтобы понять, видит ли Vosk хоть что-то
                    string partialRaw = _recognizer.PartialResult();
                    System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] 👂 ЧАСТИЧНО (raw): {partialRaw}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] ❌ Ошибка обработки: {ex.Message}");
            }
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

                    string currentMode = _isWaitingForWakeWord ? "ОЖИДАНИЕ СЛОВА" : "ОЖИДАНИЕ КОМАНДЫ";
                    System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] 🎯 РАСПОЗНАНО: '{text}' | Режим: {currentMode}");

                    if (_isWaitingForWakeWord)
                    {
                        if (text.ToLower().Contains("джарвис") || text.ToLower().Contains("jarvis"))
                        {
                            System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] ✅ Услышал 'Джарвис'!");
                            _isWaitingForWakeWord = false;
                            _isWaitingForCommand = true;
                            OnWakeWordDetected?.Invoke();
                        }
                    }
                    else if (_isWaitingForCommand)
                    {
                        if (text.ToLower().Contains("стоп") || text.ToLower().Contains("отмена") || text.ToLower().Contains("хватит"))
                        {
                            System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] 🛑 Отмена.");
                            ResetToWakeWordMode();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] 🚀 Команда: '{text}'");
                            OnCommandRecognized?.Invoke(text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Vosk DEBUG] ❌ Ошибка парсинга: {ex.Message}");
            }
        }

        public async Task SpeakAndWaitAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            _isSpeaking = true;
            text = text.Replace("*", "").Replace("#", "").Replace("[", "").Replace("]", "");

            var tcs = new TaskCompletionSource<bool>();
            EventHandler<SpeakCompletedEventArgs> handler = null;

            handler = (sender, e) =>
            {
                _synthesizer.SpeakCompleted -= handler;
                _isSpeaking = false;
                tcs.SetResult(true);
            };

            _synthesizer.SpeakCompleted += handler;
            _synthesizer.SpeakAsync(text);

            await tcs.Task;
            System.Diagnostics.Debug.WriteLine("[Vosk DEBUG] 🗣 Джарвис закончил говорить.");
        }

        public void Dispose()
        {
            StopListening();
            _postResponseTimer?.Dispose();
            _recognizer?.Dispose();
            _model?.Dispose();
            _synthesizer?.Dispose();
        }
    }
}