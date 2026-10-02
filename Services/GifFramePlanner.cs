namespace OpIlGen.Services;

/// <summary>Как заполнять промежутки между ключевыми кадрами GIF.</summary>
public enum GifTransition
{
    /// <summary>Только ключевые кадры, без промежуточных.</summary>
    None,

    /// <summary>Между соседними кадрами добавляются промежуточные: значения параметров меняются плавно.</summary>
    Smooth,

    /// <summary>Каждый кадр плавно появляется из чёрного и гаснет в чёрный (fade-in / fade-out).</summary>
    Fade
}

/// <summary>Один кадр итоговой анимации: значения параметров преобразователя и яркость (1 - без затемнения).</summary>
public sealed record GifFrameSpec(TransformerVariable[] Variables, double Brightness);

/// <summary>Строит итоговый список кадров анимации из ключевых кадров и выбранного перехода.</summary>
public static class GifFramePlanner
{
    private const double FullBrightness = 1.0;

    /// <param name="keyframes">Значения параметров для каждого ключевого кадра (в одном и том же порядке).</param>
    /// <param name="transition">Вид перехода между кадрами.</param>
    /// <param name="transitionFrames">Сколько промежуточных кадров добавлять на каждый переход.</param>
    public static IReadOnlyList<GifFrameSpec> Plan(
        IReadOnlyList<TransformerVariable[]> keyframes,
        GifTransition transition,
        int transitionFrames)
    {
        int steps = Math.Max(0, transitionFrames);
        var plan = new List<GifFrameSpec>();

        for (int i = 0; i < keyframes.Count; i++)
        {
            var keyframe = keyframes[i];

            if (transition == GifTransition.Fade)
            {
                // Появление из чёрного, кадр целиком, затухание. Яркость никогда не равна 0 или 1 на промежуточных кадрах
                for (int s = 1; s <= steps; s++)
                {
                    plan.Add(new GifFrameSpec(keyframe, s / (steps + 1.0)));
                }

                plan.Add(new GifFrameSpec(keyframe, FullBrightness));

                for (int s = 1; s <= steps; s++)
                {
                    plan.Add(new GifFrameSpec(keyframe, (steps + 1 - s) / (steps + 1.0)));
                }
            }
            else
            {
                plan.Add(new GifFrameSpec(keyframe, FullBrightness));

                if (transition == GifTransition.Smooth && i + 1 < keyframes.Count)
                {
                    for (int s = 1; s <= steps; s++)
                    {
                        var variables = Interpolate(keyframe, keyframes[i + 1], s / (steps + 1.0));
                        plan.Add(new GifFrameSpec(variables, FullBrightness));
                    }
                }
            }
        }

        return plan;
    }

    /// <summary>Сколько кадров получится в GIF (совпадает с длиной списка из <see cref="Plan"/>).</summary>
    public static int CountFrames(int keyframeCount, GifTransition transition, int transitionFrames)
    {
        if (keyframeCount <= 0) return 0;

        int steps = Math.Max(0, transitionFrames);

        return transition switch
        {
            GifTransition.Smooth => keyframeCount + (keyframeCount - 1) * steps,
            GifTransition.Fade => keyframeCount * (2 * steps + 1),
            _ => keyframeCount
        };
    }

    /// <summary>Линейная интерполяция значений параметров (сопоставление по Key). t = 0 - from, t = 1 - to.</summary>
    private static TransformerVariable[] Interpolate(TransformerVariable[] from, TransformerVariable[] to, double t)
    {
        return from.Select(a =>
        {
            var b = to.FirstOrDefault(v => v.Key == a.Key);
            double value = b is null ? a.Value : a.Value + (b.Value - a.Value) * t;
            return a.WithValue(value);
        }).ToArray();
    }
}
