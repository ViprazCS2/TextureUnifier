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
        changed |= ApplyTexture(config.Grass.BaseColor, GrassDiffuse, linear: false);
        changed |= ApplyTexture(config.Grass.Normal, GrassNormal, linear: true, config.Grass.NormalStrength);
        changed |= ApplyTexture(config.Dirt.BaseColor, DirtDiffuse, linear: false);
        changed |= ApplyTexture(config.Dirt.Normal, DirtNormal, linear: true, config.Dirt.NormalStrength);
        changed |= ApplyTexture(config.Rock.BaseColor, RockDiffuse, linear: false);
        changed |= ApplyTexture(config.Rock.Normal, RockNormal, linear: true, config.Rock.NormalStrength);

        if (ApplyTiling(config))
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

    private bool ApplyTexture(string relativePath, int shaderId, bool linear, float normalStrength = 1f)
    {
        Texture2D? texture = _textureLoader.LoadTexture(_rootPath, relativePath, linear, normalStrength);
        if (texture == null)
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
