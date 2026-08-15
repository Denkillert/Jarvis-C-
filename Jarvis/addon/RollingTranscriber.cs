using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vosk;

namespace Jarvis
{
    public class RollingTranscriber : IDisposable
    {
        private readonly Model _model;
        private readonly VoskRecognizer _recognizer;
        private readonly Queue<byte[]> _audioBuffer = new();
        private const int CHUNK_SIZE = 4000; // ~250ms at 16kHz

        public RollingTranscriber(Model model)
        {
            _model = model;
            _recognizer = new VoskRecognizer(model, 16000.0f);
        }

        public async Task<string> TranscribeChunkAsync(byte[] chunk)
        {
            // Добавляем чанк в буфер
            lock (_audioBuffer)
            {
                _audioBuffer.Enqueue(chunk);

                // Ограничиваем буфер последними 30 секундами
                while (_audioBuffer.Count > 30)
                {
                    _audioBuffer.Dequeue();
                }
            }

            // Обрабатываем через Vosk
            var result = _recognizer.AcceptWaveform(chunk, chunk.Length);

            if (result)
            {
                var json = _recognizer.Result();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _recognizer.Reset(); // Сбрасываем для нового сегмента
                        return text;
                    }
                }
            }

            return null;
        }

        public void Dispose()
        {
            _recognizer?.Dispose();
        }
    }
}