using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace OpIlGen.Localization;

/// <summary>Язык интерфейса: код (ru, en) и название на самом языке.</summary>
public sealed record LanguageInfo(string Code, string NativeName)
{
    /// <summary>Текст для списка выбора, например "RU — Русский".</summary>
    public string DisplayName => $"{Code.ToUpperInvariant()} — {NativeName}";
}

public interface ILocalizationService
{
    /// <summary>Доступные языки: язык по умолчанию первым, остальные по алфавиту.</summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    LanguageInfo CurrentLanguage { get; }

    /// <summary>Возникает после смены языка.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>Переключает язык. Неизвестный код - язык по умолчанию.</summary>
    void SetLanguage(string code);

    /// <summary>
    /// Строка на текущем языке. Если её нет, берётся строка языка по умолчанию, если и там нет - сам ключ.
    /// </summary>
    string Get(string key);

    /// <summary>Строка на текущем языке с подстановкой аргументов ({0}, {1}...).</summary>
    string Format(string key, params object[] args);

    /// <summary>Строка, которая сама обновляется при смене языка (для привязок в XAML).</summary>
    LocalizedText GetObservable(string key);
}

/// <summary>Текст, обновляющийся при смене языка. Используется как источник привязки в XAML.</summary>
public sealed class LocalizedText : INotifyPropertyChanged
{
    private string _value;

    public LocalizedText(string value)
    {
        _value = value;
    }

    public string Value
    {
        get => _value;
        internal set
        {
            if (_value == value) return;

            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Локализация на основе JSON-файлов (Localization/ru.json, en.json...), встроенных в сборку.
/// Чтобы добавить язык, достаточно добавить файл с кодом языка в имени (например, de.json) - он подхватится сам.
/// Обязательный ключ language.name - название языка на нём самом.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    public const string DefaultLanguageCode = "ru";

    private const string LanguageNameKey = "language.name";
    private const string ResourceExtension = ".json";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _dictionaries;
    private readonly IReadOnlyDictionary<string, string> _fallback;
    private readonly Dictionary<string, LocalizedText> _observables = new();
    private readonly List<LanguageInfo> _languages;
    private IReadOnlyDictionary<string, string> _current;

    /// <param name="dictionaries">Строки каждого языка: код языка -> (ключ -> текст).</param>
    /// <param name="defaultLanguageCode">Язык по умолчанию: он же запасной для ненайденных ключей.</param>
    public LocalizationService(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> dictionaries,
        string defaultLanguageCode = DefaultLanguageCode)
    {
        if (!dictionaries.TryGetValue(defaultLanguageCode, out var fallback))
        {
            throw new ArgumentException($"There are no strings for the default language \"{defaultLanguageCode}\".", nameof(dictionaries));
        }

        _dictionaries = dictionaries;
        _fallback = fallback;
        _current = fallback;

        _languages = dictionaries.Keys
            .OrderBy(code => code == defaultLanguageCode ? 0 : 1)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Select(code => new LanguageInfo(
                code,
                dictionaries[code].TryGetValue(LanguageNameKey, out var name) ? name : code))
            .ToList();

        CurrentLanguage = _languages[0];
    }

    /// <summary>Загружает все языки из JSON-файлов, встроенных в сборку (ресурсы вида OpIlGen.Localization.ru.json).</summary>
    public static LocalizationService CreateFromEmbeddedResources()
    {
        var assembly = typeof(LocalizationService).Assembly;
        string prefix = typeof(LocalizationService).Namespace + ".";

        var dictionaries = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(ResourceExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string code = resourceName.Substring(prefix.Length, resourceName.Length - prefix.Length - ResourceExtension.Length);

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            dictionaries[code] = ParseLanguageFile(reader.ReadToEnd());
        }

        return new LocalizationService(dictionaries);
    }

    /// <summary>Разбирает JSON вида { "ключ": "текст", ... }. Комментарии и лишние запятые допускаются.</summary>
    public static IReadOnlyDictionary<string, string> ParseLanguageFile(string json)
    {
        var options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options)
               ?? new Dictionary<string, string>();
    }

    public IReadOnlyList<LanguageInfo> Languages => _languages;

    public LanguageInfo CurrentLanguage { get; private set; }

    public event EventHandler? LanguageChanged;

    public void SetLanguage(string code)
    {
        var language = _languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                       ?? _languages[0];

        if (language == CurrentLanguage)
        {
            return;
        }

        CurrentLanguage = language;
        _current = _dictionaries[language.Code];

        foreach (var (key, text) in _observables)
        {
            text.Value = Get(key);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        if (_current.TryGetValue(key, out var text)) return text;
        if (_fallback.TryGetValue(key, out var fallbackText)) return fallbackText;
        return key;
    }

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public LocalizedText GetObservable(string key)
    {
        if (!_observables.TryGetValue(key, out var text))
        {
            text = new LocalizedText(Get(key));
            _observables[key] = text;
        }

        return text;
    }
}
