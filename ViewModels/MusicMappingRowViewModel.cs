using CommunityToolkit.Mvvm.ComponentModel;
using OpIlGen.Localization;
using OpIlGen.Services;

namespace OpIlGen.ViewModels;

/// <summary>Вариант источника звука в выпадающем списке.</summary>
public sealed record AudioSourceOption(AudioFeature Source, string Label);

/// <summary>Строка таблицы зависимостей: параметр преобразователя + выбранная характеристика звука.</summary>
public partial class MusicMappingRowViewModel : ObservableObject
{
    private double _fixedValue;

    public MusicMappingRowViewModel(
        TransformerVariable variable,
        ILocalizationService localizer,
        IReadOnlyList<AudioSourceOption> options)
    {
        Variable = variable;
        Name = localizer.GetName(variable);
        Description = localizer.GetDescription(variable);
        Options = options;
        _fixedValue = variable.DefaultValue;
    }

    /// <summary>Привязка или фиксированное значение изменились (нужно перерисовать картинку, если музыка не играет).</summary>
    public event EventHandler? FixedStateChanged;

    public TransformerVariable Variable { get; }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<AudioSourceOption> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixed))]
    private AudioFeature _selectedSource = AudioFeature.None;

    /// <summary>Выбрано «Фиксированное значение»: показываем поле ввода.</summary>
    public bool IsFixed => SelectedSource == AudioFeature.Fixed;

    /// <summary>
    /// Значение для режима «Фиксированное значение». Ограничивается диапазоном параметра и округляется до его шага.
    /// Событие изменения поднимается всегда, чтобы поле ввода показало исправленное значение.
    /// </summary>
    public double FixedValue
    {
        get => _fixedValue;
        set
        {
            _fixedValue = Normalize(value);
            OnPropertyChanged();
            if (IsFixed)
            {
                FixedStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Текущее значение параметра во время воспроизведения.</summary>
    [ObservableProperty]
    private string _currentValueText = string.Empty;

    partial void OnSelectedSourceChanged(AudioFeature oldValue, AudioFeature newValue)
    {
        // Перерисовка нужна, только если фиксированное значение включилось или выключилось
        if (oldValue == AudioFeature.Fixed || newValue == AudioFeature.Fixed)
        {
            FixedStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private double Normalize(double value)
    {
        if (double.IsNaN(value))
        {
            return Variable.DefaultValue;
        }

        double min = Variable.MinValue;
        double max = Variable.MaxValue;
        if (Variable.Step > 0)
        {
            value = min + Math.Round((value - min) / Variable.Step) * Variable.Step;
        }

        return Math.Clamp(value, min, max);
    }
}
