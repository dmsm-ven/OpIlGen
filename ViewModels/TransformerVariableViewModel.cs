using CommunityToolkit.Mvvm.ComponentModel;
using OpIlGen.Localization;
using OpIlGen.Services;

namespace OpIlGen.ViewModels;

/// <summary>Обёртка над <see cref="TransformerVariable"/> для UI: уведомляет об изменении значения.</summary>
public partial class TransformerVariableViewModel : ObservableObject
{
    private readonly TransformerVariable _definition;
    private readonly Action _onValueChanged;
    private readonly ILocalizationService _localizer;

    public TransformerVariableViewModel(TransformerVariable definition, Action onValueChanged, ILocalizationService localizer)
    {
        _definition = definition;
        _onValueChanged = onValueChanged;
        _localizer = localizer;

        // Название и описание обновляются при смене языка
        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
        };

        _value = definition.DefaultValue;
    }

    public string Name => _localizer.GetName(_definition);
    public string Key => _definition.Key;
    public string Description => _localizer.GetDescription(_definition);
    public double MinValue => _definition.MinValue;
    public double MaxValue => _definition.MaxValue;
    public double Step => _definition.Step;

    [ObservableProperty]
    private double _value;

    partial void OnValueChanged(double value) => _onValueChanged();

    /// <summary>Копия параметра с выбранным пользователем значением (для передачи в Transform).</summary>
    public TransformerVariable ToModel() => _definition.WithValue(Value);
}
