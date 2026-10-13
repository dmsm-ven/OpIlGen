namespace OpIlGen.Services;

/// <summary>
/// Подбирает масштаб превью по времени кадра: если кадр не укладывается в бюджет (интервал между кадрами),
/// разрешение ступенчато уменьшается, при запасе - возвращается. Стоимость кадра примерно пропорциональна
/// площади, поэтому при выборе шага вверх время оценивается как <c>время * (масштаб_выше / масштаб_сейчас)^2</c>.
/// Не потокобезопасен: вызывается из одного потока (UI).
/// </summary>
public sealed class AdaptiveResolutionController
{
    /// <summary>Ступени масштаба относительно базового размера превью.</summary>
    private static readonly double[] Scales = [1.0, 0.8, 0.64, 0.5, 0.4];

    /// <summary>Если сглаженное время кадра больше этой доли бюджета - уменьшаем разрешение.</summary>
    private const double LowerAbove = 0.85;

    /// <summary>Повышаем разрешение, только если прогноз времени на ступень выше не больше этой доли бюджета.</summary>
    private const double RaiseBelow = 0.6;

    /// <summary>Доля нового замера в скользящем среднем.</summary>
    private const double Smoothing = 0.25;

    /// <summary>Сколько кадров выждать после изменения: перед понижением (быстро) и повышением (осторожно, чтобы не дёргалось).</summary>
    private const int FramesBeforeLowering = 6;
    private const int FramesBeforeRaising = 40;

    private double _averageMs;
    private bool _hasAverage;
    private int _framesSinceChange;
    private int _level;

    // Первый кадр после запуска или смены размера дороже обычных (строится кэш входа): в статистику не берём
    private bool _skipNext = true;

    /// <summary>Текущий масштаб (1 = базовый размер).</summary>
    public double Scale => Scales[_level];

    /// <summary>Возвращает полный размер и забывает накопленную статистику.</summary>
    public void Reset()
    {
        _level = 0;
        _hasAverage = false;
        _averageMs = 0;
        _framesSinceChange = 0;
        _skipNext = true;
    }

    /// <summary>
    /// Учитывает время очередного кадра. Возвращает true, если масштаб изменился.
    /// </summary>
    /// <param name="frameMs">Сколько заняло построение и показ кадра, мс.</param>
    /// <param name="budgetMs">Бюджет кадра (интервал при выбранном FPS), мс.</param>
    /// <param name="minScale">Ниже этого масштаба не опускаемся (задаёт нижнюю границу размера в пикселях).</param>
    public bool Report(double frameMs, double budgetMs, double minScale)
    {
        if (_skipNext)
        {
            _skipNext = false;
            return false;
        }

        _averageMs = _hasAverage ? _averageMs + (frameMs - _averageMs) * Smoothing : frameMs;
        _hasAverage = true;
        _framesSinceChange++;

        int lowest = 0;
        while (lowest + 1 < Scales.Length && Scales[lowest + 1] >= minScale)
        {
            lowest++;
        }

        if (_averageMs > budgetMs * LowerAbove && _level < lowest && _framesSinceChange >= FramesBeforeLowering)
        {
            ChangeLevel(_level + 1);
            return true;
        }

        if (_level > 0 && _framesSinceChange >= FramesBeforeRaising)
        {
            double ratio = Scales[_level - 1] / Scales[_level];
            if (_averageMs * ratio * ratio < budgetMs * RaiseBelow)
            {
                ChangeLevel(_level - 1);
                return true;
            }
        }

        return false;
    }

    private void ChangeLevel(int newLevel)
    {
        // Старое среднее относилось к другому размеру: пересчитываем его по площади, чтобы не терять оценку
        double ratio = Scales[newLevel] / Scales[_level];
        _averageMs *= ratio * ratio;
        _level = newLevel;
        _framesSinceChange = 0;
        _skipNext = true;
    }
}
