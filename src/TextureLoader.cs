using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Colossal.Logging;
using UnityEngine;

namespace TextureUnifier;

internal sealed class TextureLoader
{
    private readonly ILog _log;
    private readonly Dictionary<string, LoadedTexture> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warnedMissingTextures = new(StringComparer.OrdinalIgnoreCase);

    public TextureLoader(ILog log)
    {
        _log = log;
    }

    public Texture2D? LoadTexture(string rootPath, string relativeOrAbsolutePath, bool linear, float normalStrength = 1f)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            return null;
        }

        string path = Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(rootPath, relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            if (_warnedMissingTextures.Add(path))
            {
                _log.Warn($"Texture Unifier texture missing: {path}");
            }

            return null;
        }

        string extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            _log.Warn($"Texture Unifier can only load png/jpg textures, skipped: {path}");
            return null;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(path);
        float clampedNormalStrength = Mathf.Max(0f, normalStrength);
        string cacheKey = $"{path}|linear={linear}|normalStrength={clampedNormalStrength.ToString("0.###", CultureInfo.InvariantCulture)}";

        if (_cache.TryGetValue(cacheKey, out var loaded) && loaded.WriteTime == writeTime)
        {
            return loaded.Texture;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear);
            if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: false))
            {
                UnityEngine.Object.Destroy(texture);
                _log.Warn($"Texture Unifier could not decode image: {path}");
                return null;
            }

            if (linear && !Mathf.Approximately(clampedNormalStrength, 1f))
            {
                ApplyNormalStrength(texture, clampedNormalStrength);
            }

            texture.name = Path.GetFileNameWithoutExtension(path);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);

            if (_cache.TryGetValue(cacheKey, out var previous) && previous.Texture != null)
            {
                UnityEngine.Object.Destroy(previous.Texture);
            }

            _cache[cacheKey] = new LoadedTexture(texture, writeTime);
            _log.Info($"Texture Unifier loaded {path}");
            return texture;
        }
        catch (Exception ex)
        {
            _log.Warn($"Texture Unifier failed to load {path}: {ex.Message}");
            return null;
        }
    }

    private static void ApplyNormalStrength(Texture2D texture, float strength)
    {
        var pixels = texture.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 pixel = pixels[i];
            float x = (pixel.r / 255f) * 2f - 1f;
            float y = (pixel.g / 255f) * 2f - 1f;
            float z = (pixel.b / 255f) * 2f - 1f;

            var normal = new Vector3(x * strength, y * strength, z);
            if (normal.sqrMagnitude > 0.000001f)
            {
                normal.Normalize();
            }
            else
            {
                normal = Vector3.forward;
            }

            pixels[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((normal.y * 0.5f + 0.5f) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((normal.z * 0.5f + 0.5f) * 255f), 0, 255),
                pixel.a);
        }

        texture.SetPixels32(pixels);
    }

    public void Dispose()
    {
        foreach (var loaded in _cache.Values)
        {
            if (loaded.Texture != null)
            {
                UnityEngine.Object.Destroy(loaded.Texture);
            }
        }

        _cache.Clear();
    }

    private readonly struct LoadedTexture
    {
        public LoadedTexture(Texture2D texture, DateTime writeTime)
        {
            Texture = texture;
            WriteTime = writeTime;
        }

        public Texture2D Texture { get; }

        public DateTime WriteTime { get; }
    }
}
