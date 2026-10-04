using NAudio.Dsp;
using NAudio.Wave;

namespace OpIlGen.Services;

/// <summary>Характеристика звука, от которой может зависеть параметр преобразователя.</summary>
public enum AudioFeature
{
    /// <summary>Не зависит от звука (параметр остаётся со значением по умолчанию).</summary>
    None,

    /// <summary>Сила басов (примерно 20-250 Гц).</summary>
    Bass
}

/// <summary>
/// Результат анализа трека: уровень каждой характеристики (0..1) через равные промежутки времени.
/// Уровень уже нормализован скользящим окном: 1 = самое сильное значение за последние секунды.
/// </summary>
public sealed class AudioTrackAnalysis
{
    private readonly float[] _bass;
    private readonly double _hopSeconds;
    private readonly double _frameOffsetSeconds;

    public AudioTrackAnalysis(float[] bass, double hopSeconds, double frameOffsetSeconds)
    {
        _bass = bass;
        _hopSeconds = hopSeconds;
        _frameOffsetSeconds = frameOffsetSeconds;
    }

    /// <summary>Уровень характеристики (0..1) в момент <paramref name="position"/> трека.</summary>
    public double GetLevel(AudioFeature feature, TimeSpan position)
    {
        float[]? data = feature switch
        {
            AudioFeature.Bass => _bass,
            _ => null
        };

        if (data is null || data.Length == 0)
        {
            return 0;
        }

        // Кадр i описывает момент (i + 1) * hop - offset
        double index = (position.TotalSeconds + _frameOffsetSeconds) / _hopSeconds - 1;
        if (index <= 0)
        {
            return data[0];
        }

        if (index >= data.Length - 1)
        {
            return data[^1];
        }

        int i = (int)index;
        double fraction = index - i;
        return data[i] * (1 - fraction) + data[i + 1] * fraction;
    }
}

public interface IAudioAnalysisService
{
    /// <summary>Декодирует аудиофайл и считает уровни характеристик по времени (выполняется в фоне).</summary>
    Task<AudioTrackAnalysis> AnalyzeAsync(string path, CancellationToken cancellationToken);
}

public sealed class AudioAnalysisService : IAudioAnalysisService
{
    private const int FftPower = 12;
    private const int FftSize = 1 << FftPower;

    /// <summary>Шаг анализа. Чаще, чем обновляется картинка, чтобы можно было интерполировать.</summary>
    private const double HopSeconds = 0.05;

    private const double BassLowHz = 20;
    private const double BassHighHz = 250;

    /// <summary>За сколько последних секунд ищется максимум для нормализации.</summary>
    private const double NormalizationWindowSeconds = 5;

    /// <summary>Нижний порог максимума (доля от пика всего трека), чтобы тишина не раздувалась в шум.</summary>
    private const double NoiseFloorFraction = 0.05;

    public Task<AudioTrackAnalysis> AnalyzeAsync(string path, CancellationToken cancellationToken)
        => Task.Run(() => Analyze(path, cancellationToken), cancellationToken);

    private static AudioTrackAnalysis Analyze(string path, CancellationToken cancellationToken)
    {
        using var reader = new MediaFoundationReader(path);
        ISampleProvider samples = reader.ToSampleProvider();

        int channels = samples.WaveFormat.Channels;
        int sampleRate = samples.WaveFormat.SampleRate;
        int hop = Math.Max(1, (int)(sampleRate * HopSeconds));

        int lowBin = Math.Max(1, (int)Math.Ceiling(BassLowHz * FftSize / sampleRate));
        int highBin = Math.Min(FftSize / 2 - 1, (int)Math.Floor(BassHighHz * FftSize / sampleRate));
        highBin = Math.Max(highBin, lowBin);

        var hann = new double[FftSize];
        for (int n = 0; n < FftSize; n++)
        {
            hann[n] = 0.5 * (1 - Math.Cos(2 * Math.PI * n / (FftSize - 1)));
        }

        var readBuffer = new float[hop * channels];
        var window = new float[FftSize];
        var fft = new Complex[FftSize];
        var raw = new List<float>();

        int read;
        while ((read = samples.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int frames = read / channels;
            if (frames == 0)
            {
                break;
            }

            // Сдвигаем окно и дописываем новые моно-отсчёты (среднее по каналам)
            Array.Copy(window, frames, window, 0, FftSize - frames);
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += readBuffer[f * channels + c];
                }
                window[FftSize - frames + f] = sum / channels;
            }

            for (int n = 0; n < FftSize; n++)
            {
                fft[n].X = (float)(window[n] * hann[n]);
                fft[n].Y = 0;
            }

            FastFourierTransform.FFT(true, FftPower, fft);

            double energy = 0;
            for (int bin = lowBin; bin <= highBin; bin++)
            {
                energy += fft[bin].X * fft[bin].X + fft[bin].Y * fft[bin].Y;
            }
            raw.Add((float)Math.Sqrt(energy));
        }

        var normalized = NormalizeWithSlidingWindow(raw);
        double frameOffset = FftSize / 2.0 / sampleRate;
        return new AudioTrackAnalysis(normalized, HopSeconds, frameOffset);
    }

    /// <summary>Делит каждое значение на максимум за последние секунды (с нижним порогом).</summary>
    private static float[] NormalizeWithSlidingWindow(List<float> raw)
    {
        int count = raw.Count;
        var result = new float[count];
        if (count == 0)
        {
            return result;
        }

        float globalPeak = raw.Max();
        float floor = (float)(globalPeak * NoiseFloorFraction);
        int windowFrames = Math.Max(1, (int)(NormalizationWindowSeconds / HopSeconds));

        for (int i = 0; i < count; i++)
        {
            float max = floor;
            for (int j = Math.Max(0, i - windowFrames + 1); j <= i; j++)
            {
                if (raw[j] > max)
                {
                    max = raw[j];
                }
            }

            result[i] = max > 0 ? Math.Clamp(raw[i] / max, 0f, 1f) : 0f;
        }

        return result;
    }
}
