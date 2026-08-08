using System.Collections.Generic;
using UnityEngine;

namespace SpaceWeave.Output
{
public static class SpaceWeaveFallbackPattern
{
    const int Width = 512;
    const int Height = 256;

    static readonly Dictionary<char, byte[]> Glyphs =
        new Dictionary<char, byte[]>
        {
            [' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 },
            ['-'] = new byte[] { 0, 0, 0, 31, 0, 0, 0 },
            ['0'] = new byte[] { 14, 17, 19, 21, 25, 17, 14 },
            ['1'] = new byte[] { 4, 12, 4, 4, 4, 4, 14 },
            ['2'] = new byte[] { 14, 17, 1, 2, 4, 8, 31 },
            ['3'] = new byte[] { 30, 1, 1, 14, 1, 1, 30 },
            ['4'] = new byte[] { 2, 6, 10, 18, 31, 2, 2 },
            ['5'] = new byte[] { 31, 16, 16, 30, 1, 1, 30 },
            ['6'] = new byte[] { 14, 16, 16, 30, 17, 17, 14 },
            ['7'] = new byte[] { 31, 1, 2, 4, 8, 8, 8 },
            ['8'] = new byte[] { 14, 17, 17, 14, 17, 17, 14 },
            ['9'] = new byte[] { 14, 17, 17, 15, 1, 1, 14 },
            ['A'] = new byte[] { 14, 17, 17, 31, 17, 17, 17 },
            ['B'] = new byte[] { 30, 17, 17, 30, 17, 17, 30 },
            ['C'] = new byte[] { 14, 17, 16, 16, 16, 17, 14 },
            ['D'] = new byte[] { 30, 17, 17, 17, 17, 17, 30 },
            ['E'] = new byte[] { 31, 16, 16, 30, 16, 16, 31 },
            ['F'] = new byte[] { 31, 16, 16, 30, 16, 16, 16 },
            ['G'] = new byte[] { 14, 17, 16, 23, 17, 17, 15 },
            ['H'] = new byte[] { 17, 17, 17, 31, 17, 17, 17 },
            ['I'] = new byte[] { 31, 4, 4, 4, 4, 4, 31 },
            ['K'] = new byte[] { 17, 18, 20, 24, 20, 18, 17 },
            ['L'] = new byte[] { 16, 16, 16, 16, 16, 16, 31 },
            ['M'] = new byte[] { 17, 27, 21, 21, 17, 17, 17 },
            ['N'] = new byte[] { 17, 25, 21, 19, 17, 17, 17 },
            ['O'] = new byte[] { 14, 17, 17, 17, 17, 17, 14 },
            ['P'] = new byte[] { 30, 17, 17, 30, 16, 16, 16 },
            ['Q'] = new byte[] { 14, 17, 17, 17, 21, 18, 13 },
            ['R'] = new byte[] { 30, 17, 17, 30, 20, 18, 17 },
            ['S'] = new byte[] { 15, 16, 16, 14, 1, 1, 30 },
            ['T'] = new byte[] { 31, 4, 4, 4, 4, 4, 4 },
            ['U'] = new byte[] { 17, 17, 17, 17, 17, 17, 14 },
            ['X'] = new byte[] { 17, 17, 10, 4, 10, 17, 17 },
            ['Y'] = new byte[] { 17, 17, 10, 4, 4, 4, 4 }
        };

    public static Texture2D Build(SpaceWeaveOutputMode mode, int outputWidth, int outputHeight)
    {
        var pixels = new Color32[Width * Height];
        Color32 red = new Color32(190, 35, 35, 255);
        Color32 green = new Color32(35, 165, 65, 255);
        Color32 blue = new Color32(35, 70, 190, 255);
        Color32 yellow = new Color32(210, 180, 35, 255);
        for (int y = 0; y < Height; ++y)
        {
            for (int x = 0; x < Width; ++x)
            {
                bool right = x >= Width / 2;
                bool top = y >= Height / 2;
                pixels[y * Width + x] = top
                    ? (right ? green : red)
                    : (right ? yellow : blue);
            }
        }

        FillRect(pixels, 0, 68, Width, 126, new Color32(8, 8, 12, 255));
        DrawCentred(pixels, "FALLBACK", 78, 5);
        DrawCentred(pixels, "SHADER MISSING", 119, 4);
        DrawCentred(pixels,
            ModeCode(mode) + " " + outputWidth + "X" + outputHeight, 158, 3);

        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
        {
            name = "SpaceWeave_ShaderFallback",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    static string ModeCode(SpaceWeaveOutputMode mode)
    {
        switch (mode)
        {
            case SpaceWeaveOutputMode.Equirectangular: return "EQUIRECT";
            case SpaceWeaveOutputMode.Cylindrical: return "CYLINDER";
            case SpaceWeaveOutputMode.CubemapCross: return "CROSS";
            case SpaceWeaveOutputMode.CubemapStrip: return "STRIP";
            default: return "UNSUPPORTED";
        }
    }

    static void FillRect(Color32[] pixels, int x, int y, int width, int height, Color32 colour)
    {
        for (int py = Mathf.Max(0, y); py < Mathf.Min(Height, y + height); ++py)
            for (int px = Mathf.Max(0, x); px < Mathf.Min(Width, x + width); ++px)
                pixels[py * Width + px] = colour;
    }

    static void DrawCentred(Color32[] pixels, string text, int yFromTop, int scale)
    {
        int textWidth = text.Length * 6 * scale;
        DrawText(pixels, text, (Width - textWidth) / 2, yFromTop, scale);
    }

    static void DrawText(Color32[] pixels, string text, int x, int yFromTop, int scale)
    {
        Color32 white = new Color32(255, 255, 255, 255);
        for (int index = 0; index < text.Length; ++index)
        {
            byte[] rows;
            if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[index]), out rows))
                rows = Glyphs[' '];
            for (int row = 0; row < 7; ++row)
            {
                for (int column = 0; column < 5; ++column)
                {
                    if ((rows[row] & (1 << (4 - column))) == 0) continue;
                    for (int dy = 0; dy < scale; ++dy)
                    {
                        int py = Height - 1 - (yFromTop + row * scale + dy);
                        if (py < 0 || py >= Height) continue;
                        for (int dx = 0; dx < scale; ++dx)
                        {
                            int px = x + index * 6 * scale + column * scale + dx;
                            if (px >= 0 && px < Width) pixels[py * Width + px] = white;
                        }
                    }
                }
            }
        }
    }
}
}
