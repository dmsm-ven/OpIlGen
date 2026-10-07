using System.Text.RegularExpressions;

namespace OpIlGen.Services;

/// <summary>
/// Разбор аргумента запуска с адресом изображения: <c>opilgen://open?url=https%3A%2F%2Fserver.com%2Fimage.jpg</c>.
/// Для надёжности принимается и «голый» вид <c>opilgen:https://server.com/image.jpg</c> (в том числе с обратными слэшами).
/// </summary>
public static class OpenImageRequest
{
    public const string Scheme = "opilgen";

    /// <summary>Ищет среди аргументов командной строки первый подходящий адрес изображения.</summary>
    public static bool TryFindUrl(IEnumerable<string> args, out Uri url)
    {
        foreach (var arg in args)
        {
            if (TryParse(arg, out url))
            {
                return true;
            }
        }

        url = null!;
        return false;
    }

    public static bool TryParse(string? argument, out Uri url)
    {
        url = null!;

        var text = argument?.Trim().Trim('"');
        if (string.IsNullOrEmpty(text) || !text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = text[(Scheme.Length + 1)..];
        string? value = null;

        var queryStart = rest.IndexOf('?');
        if (queryStart >= 0)
        {
            foreach (var pair in rest[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && pair[..eq].Equals("url", StringComparison.OrdinalIgnoreCase))
                {
                    value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                    break;
                }
            }
        }

        if (value is null)
        {
            value = rest.TrimStart('/', '\\').Replace('\\', '/');
            if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                value = Uri.UnescapeDataString(value);
            }

            // https:/server.com -> https://server.com
            value = Regex.Replace(value, "^(https?):/+", "$1://", RegexOptions.IgnoreCase);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            url = parsed;
            return true;
        }

        return false;
    }
}
