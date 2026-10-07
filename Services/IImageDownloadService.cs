using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace OpIlGen.Services;

public interface IImageDownloadService
{
    /// <summary>Папка, куда сохраняются скачанные изображения (%LOCALAPPDATA%\OpIlGen\Downloads).</summary>
    string DownloadDirectory { get; }

    /// <summary>Скачивает изображение по http/https ссылке и возвращает путь к локальному файлу.</summary>
    Task<string> DownloadAsync(Uri url, CancellationToken cancellationToken = default);
}

public sealed class ImageDownloadService : IImageDownloadService
{
    private const long MaxBytes = 100L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    public string DownloadDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpIlGen", "Downloads");

    public async Task<string> DownloadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            throw new NotSupportedException($"Unsupported URL scheme: {url.Scheme}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Часть серверов отдаёт картинки только со «своим» Referer (защита от хотлинка)
        request.Headers.Referrer = new Uri($"{url.Scheme}://{url.Authority}/");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The URL is not an image ({mediaType}).");
        }

        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new InvalidDataException("The image is too large.");
        }

        Directory.CreateDirectory(DownloadDirectory);
        var finalPath = Path.Combine(DownloadDirectory, BuildFileName(url, mediaType));
        var tempPath = finalPath + ".part";

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                    {
                        throw new InvalidDataException("The image is too large.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            File.Move(tempPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return finalPath;
    }

    /// <summary>Имя из ссылки + короткий хэш ссылки, чтобы разные картинки с одинаковым именем не перезаписывали друг друга.</summary>
    private static string BuildFileName(Uri url, string? mediaType)
    {
        var name = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(url.AbsolutePath));
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            name = "image";
        }

        if (name.Length > 60)
        {
            name = name[..60];
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)))[..8].ToLowerInvariant();
        return $"{name}_{hash}{GetExtension(url, mediaType)}";
    }

    private static string GetExtension(Uri url, string? mediaType)
    {
        switch (mediaType?.ToLowerInvariant())
        {
            case "image/jpeg": return ".jpg";
            case "image/png": return ".png";
            case "image/gif": return ".gif";
            case "image/bmp": return ".bmp";
            case "image/tiff": return ".tif";
        }

        var fromUrl = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        return fromUrl is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff" ? fromUrl : ".jpg";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) OpIlGen");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/*,*/*;q=0.8");
        return client;
    }
}
