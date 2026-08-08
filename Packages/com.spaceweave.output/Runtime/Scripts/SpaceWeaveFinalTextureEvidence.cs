using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Klak.Spout;
using UnityEngine;

namespace SpaceWeave.Output
{
[Serializable]
public sealed class SpaceWeavePixelSampleResult
{
    public string direction;
    public float u;
    public float v;
    public string expected;
    public string actual;
    public bool passed;
}

[Serializable]
public sealed class SpaceWeaveFinalTextureValidationResult
{
    public bool passed;
    public bool truthExpected;
    public bool truthSignaturePresent;
    public bool unexpectedMagenta;
    public bool fallbackDetected;
    public bool blankOrBlack;
    public int unexpectedMagentaPixels;
    // All four hashes exist so the receiver can tell "wrong picture" apart from
    // "right picture, wrong way up". SpaceWeave's SourceShaCompare tries the
    // canonical pair first, then the orientation variants, and reports WHICH
    // variant matched (matchedVia). With only the canonical hash a V-flip is
    // indistinguishable from garbage, so a flipped-but-correct feed reads as a
    // hard No-Go with no hint as to why.
    public string canonicalRgbSha256;
    public string rawRgbSha256;
    public string verticalFlipRgbSha256;
    public string horizontalMirrorRgbSha256;
    public string summary;
    public SpaceWeavePixelSampleResult[] samples = Array.Empty<SpaceWeavePixelSampleResult>();

    public static SpaceWeaveFinalTextureValidationResult Invalid(string reason)
    {
        return new SpaceWeaveFinalTextureValidationResult
        {
            passed = false,
            summary = reason
        };
    }
}

[Serializable]
public sealed class SpaceWeaveFinalTextureManifest
{
    public string capturedAtUtc;
    public string mode;
    public int width;
    public int height;
    public string graphicsFormat;
    public bool sRGB;
    public string finalTextureName;
    public string senderName;
    public string captureMethod;
    public string spoutSourceTextureName;
    public bool spoutSourceEqualsFinal;
    public bool keepAlpha;
    public bool truthPatternFrozen;
    public SpaceWeaveFinalTextureValidationResult validation;
    public string image;
}

public sealed class SpaceWeaveFinalTextureCapture
{
    public string pngPath;
    public string jsonPath;
    public SpaceWeaveFinalTextureValidationResult validation;
}

public static class SpaceWeaveFinalTextureEvidence
{
    struct ExpectedSample
    {
        public string name;
        public float u;
        public float v;
        public Color linearColour;

        public ExpectedSample(string name, float u, float v, Color colour)
        {
            this.name = name;
            this.u = u;
            this.v = v;
            linearColour = colour;
        }
    }

    static readonly Color Front = new Color(1f, .08f, .08f, 1f);
    static readonly Color Back = new Color(.05f, 1f, 1f, 1f);
    static readonly Color Left = new Color(.08f, .18f, 1f, 1f);
    static readonly Color Right = new Color(.08f, 1f, .12f, 1f);
    static readonly Color Top = new Color(1f, 1f, .05f, 1f);
    static readonly Color Bottom = new Color(1f, .05f, 1f, 1f);

    public static SpaceWeaveFinalTextureCapture CaptureAndSave(
        RenderTexture texture,
        SpaceWeaveOutputMode mode,
        string senderName,
        SpoutSender spout,
        string captureRoot,
        bool truthPatternFrozen)
    {
        if (texture == null || !texture.IsCreated())
            throw new InvalidOperationException("Final sender texture is not created.");

        Texture2D readback = Readback(texture);
        try
        {
            SpaceWeaveFinalTextureValidationResult validation =
                Validate(readback, mode, true);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string directory = Path.Combine(captureRoot, stamp, mode.ToString());
            Directory.CreateDirectory(directory);
            string pngPath = Path.Combine(directory, "FinalSpaceWeaveSenderRT.png");
            string jsonPath = Path.Combine(directory, "FinalSpaceWeaveSenderRT.json");
            File.WriteAllBytes(pngPath, readback.EncodeToPNG());

            var manifest = new SpaceWeaveFinalTextureManifest
            {
                capturedAtUtc = DateTime.UtcNow.ToString("O"),
                mode = mode.ToString(),
                width = texture.width,
                height = texture.height,
                graphicsFormat = texture.graphicsFormat.ToString(),
                sRGB = texture.sRGB,
                finalTextureName = texture.name,
                senderName = senderName ?? string.Empty,
                captureMethod = spout != null ? spout.captureMethod.ToString() : "Missing",
                spoutSourceTextureName =
                    spout != null && spout.sourceTexture != null
                        ? spout.sourceTexture.name
                        : string.Empty,
                spoutSourceEqualsFinal = spout != null && spout.sourceTexture == texture,
                keepAlpha = spout != null && spout.keepAlpha,
                truthPatternFrozen = truthPatternFrozen,
                validation = validation,
                image = Path.GetFileName(pngPath)
            };
            File.WriteAllText(jsonPath, JsonUtility.ToJson(manifest, true));

            // Also publish to the fixed filename the RECEIVER reads.
            //
            // Without this the receiver's whole SHA comparison was dead code:
            // SourceShaCompare::FindSenderShaEvidence looks for
            // latest_sender_evidence.json (or sender/latest.json,
            // sender_final_rt.json) and this writer only ever produced
            // <stamp>/<mode>/FinalSpaceWeaveSenderRT.json, a path it cannot know.
            // Every Go/No-Go therefore reported "sender SHA unavailable" and the
            // automatic flip diagnosis never ran on a single capture.
            //
            // Written at the capture ROOT, not under the timestamp, precisely so
            // the path is predictable; each capture overwrites it, which is what
            // "latest" means.
            WriteLatestSenderEvidence(captureRoot, mode, texture, senderName,
                validation, Path.GetFileName(pngPath));

            return new SpaceWeaveFinalTextureCapture
            {
                pngPath = pngPath,
                jsonPath = jsonPath,
                validation = validation
            };
        }
        finally
        {
            DestroyTexture(readback);
        }
    }

    public static SpaceWeaveFinalTextureValidationResult Validate(
        RenderTexture texture,
        SpaceWeaveOutputMode mode,
        bool truthExpected)
    {
        if (texture == null || !texture.IsCreated())
            return SpaceWeaveFinalTextureValidationResult.Invalid(
                "Final sender texture is missing or not created.");
        Texture2D readback = Readback(texture);
        try
        {
            return Validate(readback, mode, truthExpected);
        }
        finally
        {
            DestroyTexture(readback);
        }
    }

    public static SpaceWeaveFinalTextureValidationResult Validate(
        Texture2D readback,
        SpaceWeaveOutputMode mode,
        bool truthExpected)
    {
        if (readback == null || readback.width <= 0 || readback.height <= 0)
            return SpaceWeaveFinalTextureValidationResult.Invalid("Readback is empty.");

        Color32[] pixels = readback.GetPixels32();
        // NOTE the parameter order: (…, mirrorU, flipV).
        byte[] canonicalRgb = CanonicalTopDownRgb(
            pixels, readback.width, readback.height, false, false);
        byte[] flippedRgb = CanonicalTopDownRgb(
            pixels, readback.width, readback.height, false, true);
        byte[] mirroredRgb = CanonicalTopDownRgb(
            pixels, readback.width, readback.height, true, false);
        byte[] rawRgb = RawBottomUpRgb(pixels, readback.width, readback.height);
        var result = new SpaceWeaveFinalTextureValidationResult
        {
            truthExpected = truthExpected,
            canonicalRgbSha256 = Sha256(canonicalRgb),
            rawRgbSha256 = Sha256(rawRgb),
            verticalFlipRgbSha256 = Sha256(flippedRgb),
            horizontalMirrorRgbSha256 = Sha256(mirroredRgb)
        };

        List<ExpectedSample> expected = ExpectedMarkers(mode);
        var samples = new List<SpaceWeavePixelSampleResult>();
        int markerMatches = 0;
        foreach (ExpectedSample sample in expected)
        {
            Color actual = SampleLogical(readback, sample.u, sample.v, 2);
            Color expectedReadback = LinearToSrgb(sample.linearColour);
            bool passed = RgbDistance(actual, expectedReadback) < .22f;
            if (passed) markerMatches++;
            samples.Add(new SpaceWeavePixelSampleResult
            {
                direction = sample.name,
                u = sample.u,
                v = sample.v,
                expected = ColourText(expectedReadback),
                actual = ColourText(actual),
                passed = passed
            });
        }
        result.samples = samples.ToArray();

        bool magic = MagicMatches(readback);
        result.truthSignaturePresent = magic && markerMatches == expected.Count;
        result.blankOrBlack = IsBlankOrBlack(pixels);
        result.fallbackDetected = LooksLikeFallback(readback) && !magic;

        int magenta = 0;
        for (int y = 0; y < readback.height; ++y)
        {
            float v = 1f - (float)y / Mathf.Max(1, readback.height - 1);
            for (int x = 0; x < readback.width; ++x)
            {
                Color32 p = pixels[y * readback.width + x];
                if (!IsMagenta(p)) continue;
                float u = (float)x / Mathf.Max(1, readback.width - 1);
                if (!IsIntentionalMagenta(u, v, mode, readback.width, readback.height))
                    magenta++;
            }
        }
        result.unexpectedMagentaPixels = magenta;
        result.unexpectedMagenta = magenta > 0;
        result.passed =
            !result.blankOrBlack &&
            !result.fallbackDetected &&
            !result.unexpectedMagenta &&
            (!truthExpected || result.truthSignaturePresent);
        result.summary = result.passed
            ? "Final sender texture passed pixel validation."
            : $"signature={result.truthSignaturePresent}, fallback={result.fallbackDetected}, " +
              $"blank={result.blankOrBlack}, unexpectedMagenta={magenta}";
        return result;
    }

    public static byte[] CanonicalTopDownRgb(
        Color32[] bottomUpRgba,
        int width,
        int height,
        bool mirrorU,
        bool flipV)
    {
        if (bottomUpRgba == null || bottomUpRgba.Length < width * height)
            return Array.Empty<byte>();
        byte[] rgb = new byte[width * height * 3];
        int dst = 0;
        for (int logicalY = 0; logicalY < height; ++logicalY)
        {
            int sourceLogicalY = flipV ? height - 1 - logicalY : logicalY;
            int sourceY = height - 1 - sourceLogicalY;
            for (int logicalX = 0; logicalX < width; ++logicalX)
            {
                int sourceX = mirrorU ? width - 1 - logicalX : logicalX;
                Color32 p = bottomUpRgba[sourceY * width + sourceX];
                rgb[dst++] = p.r;
                rgb[dst++] = p.g;
                rgb[dst++] = p.b;
            }
        }
        return rgb;
    }

    /// <summary>
    /// The filename SpaceWeave's SourceShaCompare::FindSenderShaEvidence probes for.
    /// Changing it silently disables the receiver's sender/receiver SHA comparison,
    /// so it lives here as a named constant rather than a literal at the call site.
    /// </summary>
    public const string LatestSenderEvidenceFileName = "latest_sender_evidence.json";

    // Hand-built rather than JsonUtility: the receiver parses this with a small
    // string scanner (ExtractJsonStringField), and the four SHA field names below
    // are a cross-language contract with source/SourceShaCompare.cpp. Spelling them
    // out keeps the contract visible and immune to serializer formatting changes.
    static void WriteLatestSenderEvidence(
        string captureRoot,
        SpaceWeaveOutputMode mode,
        RenderTexture texture,
        string senderName,
        SpaceWeaveFinalTextureValidationResult validation,
        string imageName)
    {
        try
        {
            Directory.CreateDirectory(captureRoot);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"capturedAtUtc\": \"" + DateTime.UtcNow.ToString("O") + "\",");
            sb.AppendLine("  \"mode\": \"" + mode + "\",");
            sb.AppendLine("  \"renderTextureSize\": [" + texture.width + ", " + texture.height + "],");
            sb.AppendLine("  \"senderName\": \"" + EscapeJson(senderName ?? string.Empty) + "\",");
            sb.AppendLine("  \"canonicalRgbSha256\": \"" + (validation.canonicalRgbSha256 ?? "") + "\",");
            sb.AppendLine("  \"rawRgbSha256\": \"" + (validation.rawRgbSha256 ?? "") + "\",");
            sb.AppendLine("  \"verticalFlipRgbSha256\": \"" + (validation.verticalFlipRgbSha256 ?? "") + "\",");
            sb.AppendLine("  \"horizontalMirrorRgbSha256\": \"" + (validation.horizontalMirrorRgbSha256 ?? "") + "\",");
            sb.AppendLine("  \"passed\": " + (validation.passed ? "true" : "false") + ",");
            sb.AppendLine("  \"summary\": \"" + EscapeJson(validation.summary ?? string.Empty) + "\",");
            sb.AppendLine("  \"image\": \"" + EscapeJson(imageName ?? string.Empty) + "\"");
            sb.AppendLine("}");
            File.WriteAllText(
                Path.Combine(captureRoot, LatestSenderEvidenceFileName), sb.ToString());
        }
        catch (Exception ex)
        {
            // Never let publishing the convenience copy fail the capture itself:
            // the per-stamp manifest and PNG are already safely on disk.
            Debug.LogWarning("[SpaceWeave] Could not write " +
                LatestSenderEvidenceFileName + ": " + ex.Message);
        }
    }

    static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    // The readback buffer exactly as ReadPixels produced it (bottom-up, y=0 is the
    // image bottom), with no canonicalisation. This is the "raw" leg of the
    // comparison: if raw matches raw but canonical does not, the two sides
    // disagree about orientation convention rather than about pixels.
    public static byte[] RawBottomUpRgb(Color32[] bottomUpRgba, int width, int height)
    {
        if (bottomUpRgba == null || bottomUpRgba.Length < width * height)
            return Array.Empty<byte>();
        byte[] rgb = new byte[width * height * 3];
        int dst = 0;
        for (int i = 0; i < width * height; ++i)
        {
            Color32 p = bottomUpRgba[i];
            rgb[dst++] = p.r;
            rgb[dst++] = p.g;
            rgb[dst++] = p.b;
        }
        return rgb;
    }

    public static string Sha256(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    static Texture2D Readback(RenderTexture texture)
    {
        var readback = new Texture2D(
            texture.width, texture.height, TextureFormat.RGBA32, false, false);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        readback.Apply(false, false);
        RenderTexture.active = previous;
        return readback;
    }

    static List<ExpectedSample> ExpectedMarkers(SpaceWeaveOutputMode mode)
    {
        switch (mode)
        {
            case SpaceWeaveOutputMode.Cylindrical:
                return new List<ExpectedSample>
                {
                    new ExpectedSample("FRONT", .50f, .50f, Front),
                    new ExpectedSample("RIGHT", .75f, .50f, Right),
                    new ExpectedSample("LEFT", .25f, .50f, Left),
                    new ExpectedSample("BACK", .002f, .50f, Back),
                    new ExpectedSample("TOP", .50f, .075f, Top),
                    new ExpectedSample("BOTTOM", .50f, .925f, Bottom)
                };
            case SpaceWeaveOutputMode.CubemapCross:
                return new List<ExpectedSample>
                {
                    new ExpectedSample("FRONT", 1.5f / 4f, 1.5f / 3f, Front),
                    new ExpectedSample("RIGHT", 2.5f / 4f, 1.5f / 3f, Right),
                    new ExpectedSample("LEFT", .5f / 4f, 1.5f / 3f, Left),
                    new ExpectedSample("BACK", 3.5f / 4f, 1.5f / 3f, Back),
                    new ExpectedSample("TOP", 1.5f / 4f, .5f / 3f, Top),
                    new ExpectedSample("BOTTOM", 1.5f / 4f, 2.5f / 3f, Bottom)
                };
            case SpaceWeaveOutputMode.CubemapStrip:
                return new List<ExpectedSample>
                {
                    new ExpectedSample("FRONT", 4.5f / 6f, .5f, Front),
                    new ExpectedSample("RIGHT", .5f / 6f, .5f, Right),
                    new ExpectedSample("LEFT", 1.5f / 6f, .5f, Left),
                    new ExpectedSample("BACK", 5.5f / 6f, .5f, Back),
                    new ExpectedSample("TOP", 2.5f / 6f, .5f, Top),
                    new ExpectedSample("BOTTOM", 3.5f / 6f, .5f, Bottom)
                };
            default:
                return new List<ExpectedSample>
                {
                    new ExpectedSample("FRONT", .50f, .50f, Front),
                    new ExpectedSample("RIGHT", .75f, .50f, Right),
                    new ExpectedSample("LEFT", .25f, .50f, Left),
                    new ExpectedSample("BACK", .002f, .50f, Back),
                    new ExpectedSample("TOP", .50f, .055f, Top),
                    new ExpectedSample("BOTTOM", .50f, .945f, Bottom)
                };
        }
    }

    static bool MagicMatches(Texture2D texture)
    {
        Color[] expected =
        {
            Color.red, Color.green, Color.blue, Color.white
        };
        float[] positions = { .010f, .025f, .040f, .055f };
        for (int i = 0; i < positions.Length; ++i)
            if (RgbDistance(SampleLogical(texture, positions[i], .012f, 1), expected[i]) > .28f)
                return false;
        return true;
    }

    static bool LooksLikeFallback(Texture2D texture)
    {
        Color tl = SampleLogical(texture, .1f, .1f, 3);
        Color tr = SampleLogical(texture, .9f, .1f, 3);
        Color bl = SampleLogical(texture, .1f, .9f, 3);
        Color br = SampleLogical(texture, .9f, .9f, 3);
        return tl.r > tl.g && tr.g > tr.r && bl.b > bl.r &&
               br.r > .45f && br.g > .35f;
    }

    static bool IsBlankOrBlack(Color32[] pixels)
    {
        if (pixels == null || pixels.Length == 0) return true;
        int step = Mathf.Max(1, pixels.Length / 4096);
        int bright = 0;
        Color32 first = pixels[0];
        bool varied = false;
        for (int i = 0; i < pixels.Length; i += step)
        {
            Color32 p = pixels[i];
            if (p.r + p.g + p.b > 24) bright++;
            if (Mathf.Abs(p.r - first.r) + Mathf.Abs(p.g - first.g) +
                Mathf.Abs(p.b - first.b) > 8) varied = true;
        }
        return bright == 0 || !varied;
    }

    static bool IsMagenta(Color32 p)
    {
        return p.r > 190 && p.b > 190 && p.g < 90;
    }

    static bool IsIntentionalMagenta(
        float u, float v, SpaceWeaveOutputMode mode, int width, int height)
    {
        if (mode == SpaceWeaveOutputMode.Equirectangular ||
            mode == SpaceWeaveOutputMode.Cylindrical)
        {
            float seam = 4f / width;
            if (u <= seam || u >= 1f - seam) return true;
            float bottomV = mode == SpaceWeaveOutputMode.Cylindrical ? .925f : .945f;
            float marker = Mathf.Max(12f / width, 12f / height) * .75f;
            return Mathf.Abs(u - .5f) <= marker && Mathf.Abs(v - bottomV) <= marker;
        }
        if (mode == SpaceWeaveOutputMode.CubemapCross)
            return Mathf.Abs(u - 1.5f / 4f) <= .01f &&
                   Mathf.Abs(v - 2.5f / 3f) <= .015f;
        if (mode == SpaceWeaveOutputMode.CubemapStrip)
            return Mathf.Abs(u - 3.5f / 6f) <= .007f && Mathf.Abs(v - .5f) <= .04f;
        return false;
    }

    static Color SampleLogical(Texture2D texture, float u, float v, int radius)
    {
        int cx = Mathf.Clamp(Mathf.RoundToInt(u * (texture.width - 1)), 0, texture.width - 1);
        int cy = Mathf.Clamp(
            Mathf.RoundToInt((1f - v) * (texture.height - 1)), 0, texture.height - 1);
        Color sum = Color.black;
        int count = 0;
        for (int y = Mathf.Max(0, cy - radius);
             y <= Mathf.Min(texture.height - 1, cy + radius);
             ++y)
        {
            for (int x = Mathf.Max(0, cx - radius);
                 x <= Mathf.Min(texture.width - 1, cx + radius);
                 ++x)
            {
                sum += texture.GetPixel(x, y);
                count++;
            }
        }
        return count > 0 ? sum / count : Color.black;
    }

    static Color LinearToSrgb(Color colour)
    {
        return new Color(
            Mathf.LinearToGammaSpace(colour.r),
            Mathf.LinearToGammaSpace(colour.g),
            Mathf.LinearToGammaSpace(colour.b),
            colour.a);
    }

    static float RgbDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }

    static string ColourText(Color colour)
    {
        return $"#{ColorUtility.ToHtmlStringRGB(colour)}";
    }

    static void DestroyTexture(Texture2D texture)
    {
        if (texture == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
        else UnityEngine.Object.DestroyImmediate(texture);
    }
}

}
