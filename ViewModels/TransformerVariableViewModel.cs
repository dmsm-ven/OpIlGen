using CommunityToolkit.Mvvm.ComponentModel;
using OpIlGen.Services;

namespace OpIlGen.ViewModels;

/// <summary>Обёртка над <see cref="TransformerVariable"/> для UI: уведомляет об изменении значения.</summary>
public partial class TransformerVariableViewModel : ObservableObject
{
    private readonly TransformerVariable _definition;
    private readonly Action _onValueChanged;

    public TransformerVariableViewModel(TransformerVariable definition, Action onValueChanged)
    {
        _definition = definition;
        _onValueChanged = onValueChanged;
        _value = definition.DefaultValue;
    }

    public string Name => _definition.Name;
    public string Key => _definition.Key;
    public string Description => _definition.Description;
    public double MinValue => _definition.MinValue;
    public double MaxValue => _definition.MaxValue;
    public double Step => _definition.Step;

    [ObservableProperty]
    private double _value;

    partial void OnValueChanged(double value) => _onValueChanged();

    /// <summary>Копия параметра с выбранным пользователем значением (для передачи в Transform).</summary>
    public TransformerVariable ToModel() => new()
    {
        Name = _definition.Name,
        Key = _definition.Key,
        Description = _definition.Description,
        MinValue = _definition.MinValue,
        DefaultValue = _definition.DefaultValue,
        MaxValue = _definition.MaxValue,
        Step = _definition.Step,
        Value = Value
    };
}
