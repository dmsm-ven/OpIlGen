using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpIlGen.Localization;
using OpIlGen.Services;
using System.IO;
using System.Windows.Media;

namespace OpIlGen.ViewModels;

/// <summary>
/// Окно музыкального режима: выбор аудиофайла и воспроизведение.
/// Изображение и преобразователь переданы с главного окна (дальше будут использоваться для визуализации).
/// </summary>
public partial class MusicViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialog;
    private readonly ILocalizationService _localizer;
    private readonly MediaPlayer _player = new();

    public MusicViewModel(
        string sourcePath,
        IImageTransformer transformer,
        IFileDialogService fileDialog,
        ILocalizationService localizer)
    {
        SourcePath = sourcePath;
        Transformer = transformer;
        _fileDialog = fileDialog;
        _localizer = localizer;
        _status = localizer.Get("music.status.initial");

        _player.MediaOpened += (_, _) => Status = _localizer.Get("music.status.playing");
        _player.MediaFailed += (_, e) =>
        {
            IsPlaying = false;
            Status = string.Format(_localizer.Get("music.status.error"), e.ErrorException?.Message);
        };
        _player.MediaEnded += (_, _) =>
        {
            _player.Stop();
            IsPlaying = false;
            Status = _localizer.Get("music.status.ended");
        };
    }

    /// <summary>Путь к изображению, выбранному на главном окне (понадобится в следующих этапах).</summary>
    public string SourcePath { get; }

    /// <summary>Выбранный на главном окне преобразователь (понадобится в следующих этапах).</summary>
    public IImageTransformer Transformer { get; }

    public string SourceName => Path.GetFileName(SourcePath);

    public string TransformerName => Transformer.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MusicName))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(StopCommand))]
    private string? _musicPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(StopCommand))]
    private bool _isPlaying;

    [ObservableProperty]
    private string _status;

    public string MusicName => string.IsNullOrEmpty(MusicPath) ? "-" : Path.GetFileName(MusicPath);

    [RelayCommand]
    private void SelectMusic()
    {
        var path = _fileDialog.PickAudioFile();
        if (path is null)
        {
            return;
        }

        Stop();
        MusicPath = path;
        Status = _localizer.Get("music.status.selected");
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (string.IsNullOrEmpty(MusicPath))
        {
            return;
        }

        _player.Open(new Uri(MusicPath, UriKind.Absolute));
        _player.Play();
        IsPlaying = true;
        Status = _localizer.Get("music.status.playing");
    }

    private bool CanPlay() => !string.IsNullOrEmpty(MusicPath) && !IsPlaying;

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private void Stop()
    {
        _player.Stop();
        _player.Close();
        IsPlaying = false;
        Status = _localizer.Get("music.status.stopped");
    }

    /// <summary>Вызывается при закрытии окна: музыка не должна играть дальше.</summary>
    public void Shutdown()
    {
        _player.Stop();
        _player.Close();
    }
}
