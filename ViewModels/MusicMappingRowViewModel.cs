using CommunityToolkit.Mvvm.ComponentModel;
using OpIlGen.Localization;
using OpIlGen.Services;

namespace OpIlGen.ViewModels;

/// <summary>Вариант источника звука в выпадающем списке.</summary>
public sealed record AudioSourceOption(AudioFeature Source, string Label);

/// <summary>Строка таблицы зависимостей: параметр преобразователя + выбранная характеристика звука.</summary>
public partial class MusicMappingRowViewModel : ObservableObject
{
    public MusicMappingRowViewModel(
        TransformerVariable variable,
        ILocalizationService localizer,
        IReadOnlyList<AudioSourceOption> options)
    {
        Variable = variable;
        Name = localizer.GetName(variable);
        Description = localizer.GetDescription(variable);
        Options = options;
    }

    public TransformerVariable Variable { get; }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<AudioSourceOption> Options { get; }

    [ObservableProperty]
    private AudioFeature _selectedSource = AudioFeature.None;

    /// <summary>Текущее значение параметра во время воспроизведения.</summary>
    [ObservableProperty]
    private string _currentValueText = string.Empty;
}
