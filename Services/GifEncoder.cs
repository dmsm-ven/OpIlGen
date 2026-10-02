using System.Text;

namespace OpIlGen.Services;

/// <summary>
/// Минимальный кодировщик анимированного GIF (GIF89a): у каждого кадра своя палитра (до 256 цветов),
/// сжатие LZW, бесконечное зацикливание. Не зависит от WPF - работает с массивами пикселей Bgra32.
/// Использование: создать, вызвать <see cref="AddFrame"/> для каждого кадра, затем <see cref="Finish"/>.
/// </summary>
public sealed class GifEncoder
{
    private const int BytesPerPixel = 4;
    private const int MaxColors = 256;

    // LZW
    private const int MinCodeSizeFloor = 2;
    private const int MaxCodeBits = 12;
    private const int MaxCodes = 1 << MaxCodeBits;
    private const int SubBlockSize = 255;

    // Кванитизация методом медианного сечения по гистограмме с ColorBits бит на канал
    private const int ColorBits = 6;
    private const int ColorShift = 8 - ColorBits;
    private const int ColorLevels = 1 << ColorBits;
    private const int HistogramSize = ColorLevels * ColorLevels * ColorLevels;

    // Задержка в GIF хранится в сотых долях секунды; браузеры считают слишком малые значения (< 2) за 10
    private const int MillisecondsPerUnit = 10;
    private const int MinDelayUnits = 2;

    // Маркеры блоков GIF
    private const byte ExtensionIntroducer = 0x21;
    private const byte ApplicationExtensionLabel = 0xFF;
    private const byte GraphicControlLabel = 0xF9;
    private const byte ImageSeparator = 0x2C;
    private const byte Trailer = 0x3B;

    /// <summary>Способ обработки кадра 1 = «не удалять» (следующий кадр рисуется поверх, без прозрачности это замена).</summary>
    private const byte DisposalDoNotDispose = 1 << 2;

    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;

    public GifEncoder(Stream stream, int width, int height)
    {
        if (width is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(height));

        _stream = stream;
        _width = width;
        _height = height;

        WriteHeader();
    }

    /// <summary>Добавляет кадр: массив пикселей Bgra32 размером width*height*4 (альфа игнорируется).</summary>
    public void AddFrame(byte[] bgra, int delayMs)
    {
        if (bgra.Length != _width * _height * BytesPerPixel)
        {
            throw new ArgumentException("Размер кадра не совпадает с размером GIF.", nameof(bgra));
        }

        var (palette, colorCount, indices) = Quantize(bgra);

        // Размер таблицы цветов - степень двойки, не меньше числа цветов
        int tableBits = 1;
        while ((1 << tableBits) < colorCount) tableBits++;
        int tableSize = 1 << tableBits;
        int minCodeSize = Math.Max(MinCodeSizeFloor, tableBits);

        int delayUnits = Math.Max(MinDelayUnits, (int)Math.Round(delayMs / (double)MillisecondsPerUnit));

        // Graphic Control Extension: способ обработки и задержка
        _stream.WriteByte(ExtensionIntroducer);
        _stream.WriteByte(GraphicControlLabel);
        _stream.WriteByte(4);
        _stream.WriteByte(DisposalDoNotDispose);
        WriteUInt16(delayUnits);
        _stream.WriteByte(0); // индекс прозрачного цвета (не используется)
        _stream.WriteByte(0); // конец блока

        // Image Descriptor с локальной таблицей цветов
        _stream.WriteByte(ImageSeparator);
        WriteUInt16(0);
        WriteUInt16(0);
        WriteUInt16(_width);
        WriteUInt16(_height);
        _stream.WriteByte((byte)(0x80 | (tableBits - 1)));

        // Локальная таблица цветов (дополняется нулями до степени двойки)
        var table = new byte[tableSize * 3];
        Array.Copy(palette, table, colorCount * 3);
        _stream.Write(table, 0, table.Length);

        WriteLzw(indices, minCodeSize);
    }

    /// <summary>Завершает файл. После вызова добавлять кадры нельзя.</summary>
    public void Finish() => _stream.WriteByte(Trailer);

    private void WriteHeader()
    {
        WriteAscii("GIF89a");
        WriteUInt16(_width);
        WriteUInt16(_height);
        _stream.WriteByte(0); // глобальной таблицы цветов нет (у каждого кадра своя)
        _stream.WriteByte(0); // индекс цвета фона
        _stream.WriteByte(0); // соотношение сторон пикселя

        // Netscape Application Extension: бесконечное зацикливание
        _stream.WriteByte(ExtensionIntroducer);
        _stream.WriteByte(ApplicationExtensionLabel);
        _stream.WriteByte(11);
        WriteAscii("NETSCAPE2.0");
        _stream.WriteByte(3);
        _stream.WriteByte(1);
        WriteUInt16(0); // 0 повторов = бесконечно
        _stream.WriteByte(0);
    }

    // ---------- Квантизация цветов ----------

    private (byte[] Palette, int ColorCount, byte[] Indices) Quantize(byte[] bgra)
    {
        int pixelCount = _width * _height;
        var indices = new byte[pixelCount];

        // Если в кадре не больше 256 различных цветов, палитра точная (чёрно-белые и серые кадры)
        var colorToIndex = new Dictionary<int, byte>();
        var colors = new List<int>();
        bool exact = true;

        for (int p = 0, i = 0; p < pixelCount; p++, i += BytesPerPixel)
        {
            int color = (bgra[i + 2] << 16) | (bgra[i + 1] << 8) | bgra[i];

            if (!colorToIndex.TryGetValue(color, out byte index))
            {
                if (colorToIndex.Count == MaxColors)
                {
                    exact = false;
                    break;
                }

                index = (byte)colorToIndex.Count;
                colorToIndex[color] = index;
                colors.Add(color);
            }

            indices[p] = index;
        }

        if (!exact)
        {
            return MedianCut(bgra, pixelCount, indices);
        }

        var palette = new byte[colors.Count * 3];
        for (int c = 0; c < colors.Count; c++)
        {
            palette[c * 3] = (byte)(colors[c] >> 16);
            palette[c * 3 + 1] = (byte)(colors[c] >> 8);
            palette[c * 3 + 2] = (byte)colors[c];
        }

        return (palette, colors.Count, indices);
    }

    private static (byte[] Palette, int ColorCount, byte[] Indices) MedianCut(byte[] bgra, int pixelCount, byte[] indices)
    {
        var counts = new int[HistogramSize];
        var sumR = new long[HistogramSize];
        var sumG = new long[HistogramSize];
        var sumB = new long[HistogramSize];

        for (int p = 0, i = 0; p < pixelCount; p++, i += BytesPerPixel)
        {
            int bin = BinOf(bgra[i + 2], bgra[i + 1], bgra[i]);
            counts[bin]++;
            sumR[bin] += bgra[i + 2];
            sumG[bin] += bgra[i + 1];
            sumB[bin] += bgra[i];
        }

        var usedBins = new List<int>();
        for (int bin = 0; bin < HistogramSize; bin++)
        {
            if (counts[bin] > 0) usedBins.Add(bin);
        }
        var bins = usedBins.ToArray();

        var boxes = new List<ColorBox> { Measure(0, bins.Length, bins, counts) };

        while (boxes.Count < MaxColors)
        {
            ColorBox? best = null;
            foreach (var box in boxes)
            {
                if (box.Length > 1 && (best is null || box.Score > best.Score)) best = box;
            }
            if (best is null) break;

            // Сортируем ячейки коробки по самому длинному каналу и делим по медиане числа пикселей
            var segment = new int[best.Length];
            Array.Copy(bins, best.Start, segment, 0, best.Length);
            var keys = new int[best.Length];
            for (int i = 0; i < segment.Length; i++) keys[i] = ChannelOf(segment[i], best.Channel);
            Array.Sort(keys, segment);
            Array.Copy(segment, 0, bins, best.Start, segment.Length);

            long half = best.Count / 2;
            long accumulated = 0;
            int split = 1;
            for (int i = 0; i < segment.Length - 1; i++)
            {
                accumulated += counts[segment[i]];
                split = i + 1;
                if (accumulated >= half) break;
            }

            boxes.Remove(best);
            boxes.Add(Measure(best.Start, split, bins, counts));
            boxes.Add(Measure(best.Start + split, best.Length - split, bins, counts));
        }

        // Цвет палитры - среднее по реальным пикселям коробки
        var palette = new byte[boxes.Count * 3];
        var binToIndex = new byte[HistogramSize];

        for (int b = 0; b < boxes.Count; b++)
        {
            var box = boxes[b];
            long r = 0, g = 0, bl = 0, n = 0;

            for (int i = box.Start; i < box.Start + box.Length; i++)
            {
                int bin = bins[i];
                r += sumR[bin];
                g += sumG[bin];
                bl += sumB[bin];
                n += counts[bin];
                binToIndex[bin] = (byte)b;
            }

            palette[b * 3] = (byte)((r + n / 2) / n);
            palette[b * 3 + 1] = (byte)((g + n / 2) / n);
            palette[b * 3 + 2] = (byte)((bl + n / 2) / n);
        }

        for (int p = 0, i = 0; p < pixelCount; p++, i += BytesPerPixel)
        {
            indices[p] = binToIndex[BinOf(bgra[i + 2], bgra[i + 1], bgra[i])];
        }

        return (palette, boxes.Count, indices);
    }

    private static int BinOf(int r, int g, int b) =>
        ((r >> ColorShift) << (2 * ColorBits)) | ((g >> ColorShift) << ColorBits) | (b >> ColorShift);

    /// <summary>Значение канала ячейки гистограммы: 0 - красный, 1 - зелёный, 2 - синий.</summary>
    private static int ChannelOf(int bin, int channel) => channel switch
    {
        0 => bin >> (2 * ColorBits),
        1 => (bin >> ColorBits) & (ColorLevels - 1),
        _ => bin & (ColorLevels - 1)
    };

    private static ColorBox Measure(int start, int length, int[] bins, int[] counts)
    {
        var min = new[] { int.MaxValue, int.MaxValue, int.MaxValue };
        var max = new[] { int.MinValue, int.MinValue, int.MinValue };
        long count = 0;

        for (int i = start; i < start + length; i++)
        {
            int bin = bins[i];
            for (int c = 0; c < 3; c++)
            {
                int value = ChannelOf(bin, c);
                if (value < min[c]) min[c] = value;
                if (value > max[c]) max[c] = value;
            }
            count += counts[bin];
        }

        int channel = 0, range = max[0] - min[0];
        for (int c = 1; c < 3; c++)
        {
            if (max[c] - min[c] > range)
            {
                range = max[c] - min[c];
                channel = c;
            }
        }

        return new ColorBox(start, length, count, channel, range);
    }

    private sealed record ColorBox(int Start, int Length, long Count, int Channel, int Range)
    {
        /// <summary>Приоритет деления: большие по числу пикселей и разбросу цветов коробки делятся первыми.</summary>
        public long Score => Count * Math.Max(1, Range);
    }

    // ---------- Сжатие LZW ----------

    private void WriteLzw(byte[] indices, int minCodeSize)
    {
        _stream.WriteByte((byte)minCodeSize);

        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        int codeSize = minCodeSize + 1;
        int nextCode = endCode + 1;

        var dictionary = new Dictionary<int, int>();
        var packer = new BitPacker(_stream);

        packer.Write(clearCode, codeSize);

        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int symbol = indices[i];
            int key = (prefix << 8) | symbol;

            if (dictionary.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            packer.Write(prefix, codeSize);

            if (nextCode < MaxCodes)
            {
                dictionary[key] = nextCode++;
                if (nextCode > (1 << codeSize) && codeSize < MaxCodeBits) codeSize++;
            }
            else
            {
                // Таблица заполнена: сбрасываем её
                packer.Write(clearCode, codeSize);
                dictionary.Clear();
                codeSize = minCodeSize + 1;
                nextCode = endCode + 1;
            }

            prefix = symbol;
        }

        packer.Write(prefix, codeSize);

        // Декодер после последнего кода уже мог увеличить разрядность - код конца пишем с учётом этого
        if (nextCode >= (1 << codeSize) && codeSize < MaxCodeBits) codeSize++;
        packer.Write(endCode, codeSize);

        packer.Finish();
    }

    /// <summary>Упаковывает коды переменной разрядности (младшие биты вперёд) в под-блоки по 255 байт.</summary>
    private sealed class BitPacker
    {
        private readonly Stream _stream;
        private readonly byte[] _block = new byte[SubBlockSize];
        private int _blockLength;
        private int _bitBuffer;
        private int _bitCount;

        public BitPacker(Stream stream) => _stream = stream;

        public void Write(int code, int size)
        {
            _bitBuffer |= code << _bitCount;
            _bitCount += size;

            while (_bitCount >= 8)
            {
                AddByte(_bitBuffer & 0xFF);
                _bitBuffer >>= 8;
                _bitCount -= 8;
            }
        }

        public void Finish()
        {
            if (_bitCount > 0) AddByte(_bitBuffer & 0xFF);
            FlushBlock();
            _stream.WriteByte(0); // конец данных изображения
        }

        private void AddByte(int value)
        {
            _block[_blockLength++] = (byte)value;
            if (_blockLength == SubBlockSize) FlushBlock();
        }

        private void FlushBlock()
        {
            if (_blockLength == 0) return;
            _stream.WriteByte((byte)_blockLength);
            _stream.Write(_block, 0, _blockLength);
            _blockLength = 0;
        }
    }

    // ---------- Вспомогательное ----------

    private void WriteUInt16(int value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)(value >> 8));
    }

    private void WriteAscii(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
    }
}
