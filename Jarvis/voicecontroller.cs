using NAudio.Wave;
using System;
using System.Globalization;
using System.IO;
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
        private bool _awaitingCommand = false;
        private System.Timers.Timer _responseTimer;
        private bool _isSpeaking = false;

        public event Action<string> OnCommandRecognized;
        public event Action OnWakeWordDetected;

        public VoiceController()
        {
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

            _responseTimer = new System.Timers.Timer(5000); // 5 секунд на ответ после реплики
            _responseTimer.Elapsed += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("[Voice] ⏱ Таймер истёк - жду 'Джарвис'");
                _awaitingCommand = false;
            };
            _responseTimer.AutoReset = false;

            try
            {
                string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "ru", "vosk-model-small-ru-0.22");
                System.Diagnostics.Debug.WriteLine($"[Vosk] Путь: {modelPath}");

                if (Directory.Exists(modelPath))
                {
                    _model = new Model(modelPath);
                    _recognizer = new VoskRecognizer(_model, 16000.0f);
                    _recognizer.SetMaxAlternatives(0);
                    _recognizer.SetWords(false);
                    System.Diagnostics.Debug.WriteLine("[Vosk] ✅ Модель загружена");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Vosk] ❌ НЕ НАЙДЕНА: {modelPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Vosk] Ошибка: {ex.Message}");
            }
        }

        public void StartListening(bool wakeWordMode = true)
        {
            if (_recognizer == null) return;

            if (!_isListening)
            {
                try
                {
                    _waveIn = new WaveInEvent();
                    _waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
                    _waveIn.DataAvailable += OnDataAvailable;
                    _waveIn.StartRecording();
                    _isListening = true;

                    if (wakeWordMode)
                    {
                        _awaitingCommand = false;
                        System.Diagnostics.Debug.WriteLine("[Voice] 🔴 Слушаю слово 'Джарвис'...");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Voice] Ошибка запуска: {ex.Message}");
                }
            }
        }

        public void StartListeningForCommands()
        {
            _awaitingCommand = true;
            _responseTimer.Start();

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

            System.Diagnostics.Debug.WriteLine("[Voice] 🎤 Слушаю команду (5 сек)...");
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

            // 🔥 ГЛАВНОЕ: Игнорируем звук, пока Джарвис говорит
            if (_isSpeaking) return;

            bool result = _recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded);

            if (result)
            {
                string jsonResult = _recognizer.Result();
                ProcessRecognition(jsonResult);
            }
            else
            {
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

                    System.Diagnostics.Debug.WriteLine($"[Voice] Распознано: '{text}' (режим: {(_awaitingCommand ? "КОМАНДА" : "WAKE WORD")})");

                    if (_awaitingCommand)
                    {
                        if (text.ToLower().Contains("стоп") || text.ToLower().Contains("хватит") || text.ToLower().Contains("отмена"))
                        {
                            System.Diagnostics.Debug.WriteLine("[Voice] Услышал 'стоп' - сброс");
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

        public async Task SpeakAndWaitAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // 🔥 ВАЖНО: Блокируем микрофон, пока говорим
            _isSpeaking = true;
            text = text.Replace("*", "").Replace("#", "").Replace("[", "").Replace("]", "");

            var tcs = new TaskCompletionSource<bool>();
            EventHandler<SpeakCompletedEventArgs> handler = null;

            handler = (sender, e) =>
            {
                _synthesizer.SpeakCompleted -= handler;
                _isSpeaking = false; // Разблокируем микрофон
                tcs.SetResult(true);
            };

            _synthesizer.SpeakCompleted += handler;
            _synthesizer.SpeakAsync(text);

            await tcs.Task;
            System.Diagnostics.Debug.WriteLine("[Voice] Джарвис закончил говорить.");
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