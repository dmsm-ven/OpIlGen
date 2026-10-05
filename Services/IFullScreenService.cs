using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using OpIlGen.Views;

namespace OpIlGen.Services;

public interface IFullScreenService
{
    /// <summary>Показывает изображение в окне на весь экран.</summary>
    void Show(BitmapSource image);

    /// <summary>
    /// Показывает изображение на весь экран и обновляет его, пока окно открыто: каждый раз, когда у
    /// <paramref name="source"/> меняется свойство <paramref name="propertyName"/>, берётся новая картинка.
    /// </summary>
    /// <param name="onClosed">Вызывается после закрытия окна.</param>
    void ShowLive(
        INotifyPropertyChanged source,
        string propertyName,
        Func<BitmapSource?> imageGetter,
        Action? onClosed = null);
}

public sealed class FullScreenService : IFullScreenService
{
    public void Show(BitmapSource image)
    {
        var window = new FullScreenWindow(image)
        {
            Owner = GetOwner()
        };
        window.Show();
    }

    public void ShowLive(
        INotifyPropertyChanged source,
        string propertyName,
        Func<BitmapSource?> imageGetter,
        Action? onClosed = null)
    {
        var initial = imageGetter();
        if (initial is null)
        {
            return;
        }

        var window = new FullScreenWindow(initial)
        {
            Owner = GetOwner()
        };

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == propertyName && imageGetter() is { } image)
            {
                window.SetImage(image);
            }
        }

        source.PropertyChanged += OnPropertyChanged;
        window.Closed += (_, _) =>
        {
            source.PropertyChanged -= OnPropertyChanged;
            onClosed?.Invoke();
        };

        window.Show();
    }

    /// <summary>
    /// Владелец полноэкранного окна - окно, из которого оно открыто. Это важно для окна музыки:
    /// оно модальное, и окно, привязанное к главному окну, оказалось бы под ним.
    /// </summary>
    private static Window? GetOwner()
        => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
           ?? Application.Current.MainWindow;
}
