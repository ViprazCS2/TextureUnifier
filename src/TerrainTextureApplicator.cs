using System;
using System.Collections.Generic;
using Colossal.Logging;
using UnityEngine;

namespace TextureUnifier;

internal sealed class TerrainTextureApplicator
{
    private const string GrassDiffuseName = "colossal_TerrainGrassDiffuse";
    private const string GrassNormalName = "colossal_TerrainGrassNormal";
    private const string DirtDiffuseName = "colossal_TerrainDirtDiffuse";
    private const string DirtNormalName = "colossal_TerrainDirtNormal";
    private const string RockDiffuseName = "colossal_TerrainRockDiffuse";
    private const string RockNormalName = "colossal_TerrainRockNormal";
    private const string TilingVectorName = "colossal_TerrainTextureTiling";

    private static readonly int GrassDiffuse = Shader.PropertyToID(GrassDiffuseName);
    private static readonly int GrassNormal = Shader.PropertyToID(GrassNormalName);
    private static readonly int DirtDiffuse = Shader.PropertyToID(DirtDiffuseName);
    private static readonly int DirtNormal = Shader.PropertyToID(DirtNormalName);
    private static readonly int RockDiffuse = Shader.PropertyToID(RockDiffuseName);
    private static readonly int RockNormal = Shader.PropertyToID(RockNormalName);
    private static readonly int TilingVector = Shader.PropertyToID(TilingVectorName);

    private readonly string _rootPath;
    private readonly TextureLoader _textureLoader;
    private readonly ILog _log;
    private readonly Dictionary<int, Texture?> _originalTextures = new();
    private Vector4? _originalTiling;
    private bool _hasApplied;

    public TerrainTextureApplicator(string rootPath, TextureLoader textureLoader, ILog log)
    {
        _rootPath = rootPath;
        _textureLoader = textureLoader;
        _log = log;
    }

    public void Apply(TerrainTextureConfig config)
    {
        if (!config.Enabled)
        {
            Restore();
            return;
        }

        CacheOriginals();

        bool changed = false;
        bool hasAnyTexture = false;
        changed |= ApplyTexture(config.Grass.BaseColor, GrassDiffuse, false, out bool hasGrassBase);
        hasAnyTexture |= hasGrassBase;
        changed |= ApplyTexture(config.Grass.Normal, GrassNormal, true, config.Grass.NormalStrength, out bool hasGrassNormal);
        hasAnyTexture |= hasGrassNormal;
        changed |= ApplyTexture(config.Dirt.BaseColor, DirtDiffuse, false, out bool hasDirtBase);
        hasAnyTexture |= hasDirtBase;
        changed |= ApplyTexture(config.Dirt.Normal, DirtNormal, true, config.Dirt.NormalStrength, out bool hasDirtNormal);
        hasAnyTexture |= hasDirtNormal;
        changed |= ApplyTexture(config.Rock.BaseColor, RockDiffuse, false, out bool hasRockBase);
        hasAnyTexture |= hasRockBase;
        changed |= ApplyTexture(config.Rock.Normal, RockNormal, true, config.Rock.NormalStrength, out bool hasRockNormal);
        hasAnyTexture |= hasRockNormal;

        if (hasAnyTexture && ApplyTiling(config))
        {
            changed = true;
        }

        if (changed && !_hasApplied)
        {
            _hasApplied = true;
            _log.Info("Texture Unifier terrain textures applied.");
        }
    }

    public void Restore()
    {
        foreach (var pair in _originalTextures)
        {
            if (pair.Value != null)
            {
                Shader.SetGlobalTexture(pair.Key, pair.Value);
            }
        }

        if (_originalTiling.HasValue)
        {
            Shader.SetGlobalVector(TilingVector, _originalTiling.Value);
        }

        _originalTextures.Clear();
        _originalTiling = null;
        _hasApplied = false;
    }

    private void CacheOriginals()
    {
        CacheOriginalTexture(GrassDiffuse);
        CacheOriginalTexture(GrassNormal);
        CacheOriginalTexture(DirtDiffuse);
        CacheOriginalTexture(DirtNormal);
        CacheOriginalTexture(RockDiffuse);
        CacheOriginalTexture(RockNormal);

        if (!_originalTiling.HasValue)
        {
            _originalTiling = Shader.GetGlobalVector(TilingVector);
        }
    }

    private void CacheOriginalTexture(int shaderId)
    {
        if (!_originalTextures.ContainsKey(shaderId))
        {
            _originalTextures[shaderId] = Shader.GetGlobalTexture(shaderId);
        }
    }

    private bool ApplyTexture(string relativePath, int shaderId, bool linear, out bool textureLoaded)
    {
        return ApplyTexture(relativePath, shaderId, linear, 1f, out textureLoaded);
    }

    private bool ApplyTexture(string relativePath, int shaderId, bool linear, float normalStrength, out bool textureLoaded)
    {
        Texture2D? texture = _textureLoader.LoadTexture(_rootPath, relativePath, linear, normalStrength);
        textureLoaded = texture != null;
        if (texture == null)
        {
            return false;
        }

        if (Shader.GetGlobalTexture(shaderId) == texture)
        {
            return false;
        }

        Shader.SetGlobalTexture(shaderId, texture);
        return true;
    }

    private static bool ApplyTiling(TerrainTextureConfig config)
    {
        var current = Shader.GetGlobalVector(TilingVector);
        var next = current;

        if (config.LowFrequencyScale > 0f)
        {
            next.x = config.LowFrequencyScale;
        }

        if (config.HighFrequencyScale > 0f)
        {
            next.y = config.HighFrequencyScale;
        }

        if (config.DirtHighFrequencyScale > 0f)
        {
            next.z = config.DirtHighFrequencyScale;
        }

        if (next == current)
        {
            return false;
        }

        Shader.SetGlobalVector(TilingVector, next);
        return true;
    }
}
