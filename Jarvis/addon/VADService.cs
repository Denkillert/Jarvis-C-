using System;

namespace Jarvis
{
    /// <summary>
    /// Детектор речевой активности по энергии (RMS)
    /// Помогает понять, когда пользователь начал и закончил говорить
    /// </summary>
    public class VADService
    {
        private const float THRESHOLD = 0.02f;      // Порог энергии (настрой под свой микрофон)
        private const int SILENCE_FRAMES = 15;      // Кадров тишины для фиксации конца речи (~500ms)

        private int _silenceCounter = 0;
        private bool _isSpeaking = false;

        /// <summary>Пользователь начал говорить</summary>
        public event Action OnSpeechStart;

        /// <summary>Пользователь замолчал (речь закончилась)</summary>
        public event Action OnSpeechEnd;

        public bool IsSpeaking => _isSpeaking;

        /// <summary>
        /// Обработать чанк аудио (float32, нормализованный к [-1, 1])
        /// Вызывать из DataAvailable в VoiceController
        /// </summary>
        public void ProcessAudio(byte[] buffer, int bytesRecorded)
        {
            // Конвертируем PCM16 → float32 и считаем RMS
            int sampleCount = bytesRecorded / 2;
            if (sampleCount == 0) return;

            double sum = 0;
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                double normalized = sample / 32768.0;
                sum += normalized * normalized;
            }

            double rms = Math.Sqrt(sum / sampleCount);

            if (rms > THRESHOLD)
            {
                _silenceCounter = 0;
                if (!_isSpeaking)
                {
                    _isSpeaking = true;
                    OnSpeechStart?.Invoke();
                }
            }
            else
            {
                _silenceCounter++;
                if (_isSpeaking && _silenceCounter >= SILENCE_FRAMES)
                {
                    _isSpeaking = false;
                    _silenceCounter = 0;
                    OnSpeechEnd?.Invoke();
                }
            }
        }

        public void Reset()
        {
            _silenceCounter = 0;
            _isSpeaking = false;
        }
    }
}