using NAudio.Dsp;
using NAudio.Wave;

namespace OpIlGen.Services;

/// <summary>Характеристика звука, от которой может зависеть параметр преобразователя.</summary>
public enum AudioFeature
{
    /// <summary>Не зависит от звука (параметр остаётся со значением по умолчанию).</summary>
    None,

    /// <summary>Общая громкость сигнала (RMS).</summary>
    Loudness,

    /// <summary>Сила басов (примерно 20-250 Гц).</summary>
    Bass,

    /// <summary>Средние частоты (примерно 250-2000 Гц): голос, гитары, клавиши.</summary>
    Mid,

    /// <summary>Высокие частоты (примерно 4-16 кГц): тарелки, шипящие, "воздух".</summary>
    Treble,

    /// <summary>Ритм: резкость нарастания звука (удары, атаки нот). Реагирует на биты, а не на уровень.</summary>
    Beat,

    /// <summary>Яркость звука: где "центр тяжести" спектра (тёмный глухой звук или яркий звонкий).</summary>
    Brightness
}

/// <summary>
/// Результат анализа трека: уровень каждой характеристики (0..1) через равные промежутки времени.
/// Уровень уже нормализован скользящим окном: 1 = самое сильное значение за последние секунды.
/// </summary>
public sealed class AudioTrackAnalysis
{
    private readonly IReadOnlyDictionary<AudioFeature, float[]> _levels;
    private readonly double _hopSeconds;
    private readonly double _frameOffsetSeconds;

    public AudioTrackAnalysis(
        IReadOnlyDictionary<AudioFeature, float[]> levels,
        double hopSeconds,
        double frameOffsetSeconds)
    {
        _levels = levels;
        _hopSeconds = hopSeconds;
        _frameOffsetSeconds = frameOffsetSeconds;
    }

    /// <summary>Уровень характеристики (0..1) в момент <paramref name="position"/> трека.</summary>
    public double GetLevel(AudioFeature feature, TimeSpan position)
    {
        if (!_levels.TryGetValue(feature, out var data) || data.Length == 0)
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

    /// <summary>За сколько последних секунд ищется максимум для нормализации.</summary>
    private const double NormalizationWindowSeconds = 5;

    /// <summary>Нижний порог максимума (доля от пика всего трека), чтобы тишина не раздувалась в шум.</summary>
    private const double NoiseFloorFraction = 0.05;

    /// <summary>Для ритма учитываются изменения спектра только до этой частоты.</summary>
    private const double FluxHighHz = 10000;

    /// <summary>Для яркости учитываются частоты в этих пределах.</summary>
    private const double CentroidLowHz = 40;
    private const double CentroidHighHz = 16000;

    /// <summary>Суммарная амплитуда спектра ниже этого значения считается тишиной (яркость не меняется).</summary>
    private const double SilenceMagnitude = 1.0;

    /// <summary>Минимальный размах яркости (в октавах), чтобы малые колебания не растягивались на весь диапазон.</summary>
    private const double BrightnessMinSpanOctaves = 1.0;

    /// <summary>Полосы частот, для которых считается энергия.</summary>
    private static readonly (AudioFeature Feature, double LowHz, double HighHz)[] Bands =
    [
        (AudioFeature.Bass, 20, 250),
        (AudioFeature.Mid, 250, 2000),
        (AudioFeature.Treble, 4000, 16000)
    ];

    public Task<AudioTrackAnalysis> AnalyzeAsync(string path, CancellationToken cancellationToken)
        => Task.Run(() => Analyze(path, cancellationToken), cancellationToken);

    private static AudioTrackAnalysis Analyze(string path, CancellationToken cancellationToken)
    {
        using var reader = new MediaFoundationReader(path);
        ISampleProvider samples = reader.ToSampleProvider();

        int channels = samples.WaveFormat.Channels;
        int sampleRate = samples.WaveFormat.SampleRate;
        int hop = Math.Max(1, (int)(sampleRate * HopSeconds));
        int half = FftSize / 2;

        var bandRanges = Bands.Select(b => BinRange(b.LowHz, b.HighHz, sampleRate)).ToArray();
        var fluxRange = BinRange(CentroidLowHz, FluxHighHz, sampleRate);
        var centroidRange = BinRange(CentroidLowHz, CentroidHighHz, sampleRate);
        double binHz = (double)sampleRate / FftSize;

        var hann = new double[FftSize];
        for (int n = 0; n < FftSize; n++)
        {
            hann[n] = 0.5 * (1 - Math.Cos(2 * Math.PI * n / (FftSize - 1)));
        }

        var readBuffer = new float[hop * channels];
        var window = new float[FftSize];
        var fft = new Complex[FftSize];
        var magnitudes = new double[half];
        var previous = new double[half];
        bool hasPrevious = false;
        double lastCentroidLog = Math.Log2(1000);

        var loudness = new List<float>();
        var bandRaw = Bands.ToDictionary(b => b.Feature, _ => new List<float>());
        var beat = new List<float>();
        var brightness = new List<float>();

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

            // Громкость: RMS по окну
            double squares = 0;
            for (int n = 0; n < FftSize; n++)
            {
                squares += window[n] * window[n];
            }
            loudness.Add((float)Math.Sqrt(squares / FftSize));

            for (int n = 0; n < FftSize; n++)
            {
                fft[n].X = (float)(window[n] * hann[n]);
                fft[n].Y = 0;
            }

            FastFourierTransform.FFT(true, FftPower, fft);

            for (int k = 0; k < half; k++)
            {
                magnitudes[k] = Math.Sqrt(fft[k].X * fft[k].X + fft[k].Y * fft[k].Y);
            }

            // Энергия по полосам (басы, средние, высокие)
            for (int b = 0; b < Bands.Length; b++)
            {
                bandRaw[Bands[b].Feature].Add((float)BandEnergy(magnitudes, bandRanges[b]));
            }

            // Ритм: сумма положительных приращений (сжатого) спектра
            double flux = 0;
            if (hasPrevious)
            {
                for (int k = fluxRange.Low; k <= fluxRange.High; k++)
                {
                    double diff = Math.Sqrt(magnitudes[k]) - Math.Sqrt(previous[k]);
                    if (diff > 0)
                    {
                        flux += diff;
                    }
                }
            }
            beat.Add((float)flux);
            Array.Copy(magnitudes, previous, half);
            hasPrevious = true;

            // Яркость: спектральный центроид (в октавах, чтобы шкала была ближе к слуху)
            double weighted = 0;
            double total = 0;
            for (int k = centroidRange.Low; k <= centroidRange.High; k++)
            {
                weighted += k * binHz * magnitudes[k];
                total += magnitudes[k];
            }
            if (total >= SilenceMagnitude)
            {
                lastCentroidLog = Math.Log2(Math.Max(weighted / total, CentroidLowHz));
            }
            brightness.Add((float)lastCentroidLog);
        }

        var levels = new Dictionary<AudioFeature, float[]>
        {
            [AudioFeature.Loudness] = NormalizeWithSlidingMax(loudness),
            [AudioFeature.Beat] = NormalizeWithSlidingMax(beat),
            [AudioFeature.Brightness] = NormalizeWithSlidingRange(brightness)
        };
        foreach (var (feature, values) in bandRaw)
        {
            levels[feature] = NormalizeWithSlidingMax(values);
        }

        double frameOffset = FftSize / 2.0 / sampleRate;
        return new AudioTrackAnalysis(levels, HopSeconds, frameOffset);
    }

    private static (int Low, int High) BinRange(double lowHz, double highHz, int sampleRate)
    {
        int low = Math.Max(1, (int)Math.Ceiling(lowHz * FftSize / sampleRate));
        int high = Math.Min(FftSize / 2 - 1, (int)Math.Floor(highHz * FftSize / sampleRate));
        return (low, high);
    }

    private static double BandEnergy(double[] magnitudes, (int Low, int High) range)
    {
        double energy = 0;
        for (int k = range.Low; k <= range.High; k++)
        {
            energy += magnitudes[k] * magnitudes[k];
        }
        return Math.Sqrt(energy);
    }

    /// <summary>Делит каждое значение на максимум за последние секунды (с нижним порогом).</summary>
    private static float[] NormalizeWithSlidingMax(List<float> raw)
    {
        int count = raw.Count;
        var result = new float[count];
        if (count == 0)
        {
            return result;
        }

        float floor = (float)(raw.Max() * NoiseFloorFraction);
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

    /// <summary>
    /// Растягивает значение между минимумом и максимумом за последние секунды.
    /// Для величин, у которых нет "нуля" (яркость): минимум даёт 0, максимум даёт 1.
    /// </summary>
    private static float[] NormalizeWithSlidingRange(List<float> raw)
    {
        int count = raw.Count;
        var result = new float[count];
        int windowFrames = Math.Max(1, (int)(NormalizationWindowSeconds / HopSeconds));

        for (int i = 0; i < count; i++)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int j = Math.Max(0, i - windowFrames + 1); j <= i; j++)
            {
                min = Math.Min(min, raw[j]);
                max = Math.Max(max, raw[j]);
            }

            double span = max - min;
            double low = min;
            if (span < BrightnessMinSpanOctaves)
            {
                // Слишком ровный звук: размещаем значение вокруг середины, а не прижимаем к нулю
                low = (max + min) / 2 - BrightnessMinSpanOctaves / 2;
                span = BrightnessMinSpanOctaves;
            }

            result[i] = (float)Math.Clamp((raw[i] - low) / span, 0, 1);
        }

        return result;
    }
}
