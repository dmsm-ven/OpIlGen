using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace OpIlGen.Services;

/// <summary>
/// Запуск только одного экземпляра приложения. Первый экземпляр держит мьютекс и слушает именованный канал;
/// повторный запуск передаёт ему свои аргументы (например, ссылку opilgen://...) и завершается.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int SwRestore = 9;
    private const int AsfwAny = -1;

    private static readonly string MutexName = @"Local\OpIlGen.SingleInstance";

    private static readonly string PipeName =
        $"OpIlGen.{Environment.UserName}.{Process.GetCurrentProcess().SessionId}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirst = createdNew;
    }

    /// <summary>Это первый экземпляр приложения.</summary>
    public bool IsFirst { get; }

    /// <summary>Передаёт аргументы запущенному экземпляру. Возвращает false, если связаться с ним не удалось.</summary>
    public bool SendToFirst(string[] args)
    {
        try
        {
            // Разрешаем уже запущенному экземпляру выйти на передний план (мы только что получили фокус от пользователя)
            AllowSetForegroundWindow(AsfwAny);

            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(5000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            foreach (var arg in args)
            {
                writer.Write(arg);
                writer.Write('\n');
            }

            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Начинает принимать аргументы от повторных запусков. Обработчик вызывается из фонового потока.</summary>
    public void StartListening(Action<string[]> onArguments)
    {
        _ = Task.Run(() => ListenAsync(onArguments, _cts.Token));
    }

    private static async Task ListenAsync(Action<string[]> onArguments, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(token);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(token);

                var args = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                onArguments(args);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // Клиент оборвал соединение - ждём следующего
            }
        }
    }

    /// <summary>Разворачивает свёрнутое окно (с прежним состоянием, в том числе maximized) и выводит его на передний план.</summary>
    public static void BringToFront(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && window.WindowState == WindowState.Minimized)
        {
            ShowWindow(handle, SwRestore);
        }

        window.Show();
        window.Activate();

        // Запасной вариант, если Windows не отдала фокус: кратковременный Topmost поднимает окно поверх остальных
        var topmost = window.Topmost;
        window.Topmost = true;
        window.Topmost = topmost;
        window.Focus();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        if (IsFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Освобождение не из потока-владельца при аварийном завершении - мьютекс уйдёт вместе с процессом
            }
        }

        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
