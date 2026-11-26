namespace PurinPointer.Core;

/// <summary>
/// A 32-bit image stored top-down as B, G, R, A bytes with straight (non-premultiplied) alpha.
/// This matches WPF's <c>PixelFormats.Bgra32</c> and the byte order of 32-bit Windows DIBs.
/// </summary>
public sealed class Bitmap32
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public Bitmap32(int width, int height, byte[]? pixels = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Image must not be empty.");
        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height * 4];
        if (Pixels.Length != width * height * 4) throw new ArgumentException("Pixel buffer size does not match dimensions.", nameof(pixels));
    }

    public int Stride => Width * 4;

    public Bitmap32 Clone() => new(Width, Height, (byte[])Pixels.Clone());

    public byte Alpha(int x, int y) => Pixels[(y * Width + x) * 4 + 3];

    /// <summary>True when every pixel is fully transparent.</summary>
    public bool IsEmpty()
    {
        for (int i = 3; i < Pixels.Length; i += 4)
            if (Pixels[i] != 0) return false;
        return true;
    }
}

/// <summary>One frame of a cursor animation.</summary>
public sealed record CursorFrame(Bitmap32 Image, int DelayMs);
