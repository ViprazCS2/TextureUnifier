using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Logging;
using Game.Rendering;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace TextureUnifier;

internal sealed class FoliageVfxApplicator
{
    private const int AmbientOcclusionMaskMaxResolution = 1024;
    private const float AmbientOcclusionGrowthBoost = 4f;

    private static readonly string[] FoliageNameTokens =
    {
        "Foliage",
        "Grass"
    };

    private static readonly string[] TerrainSplatMapProperties =
    {
        "Terrain SplatMap",
        "Splatmap"
    };

    private static readonly string[] FoliageCoverageProperties =
    {
        "FoliageCoverage"
    };

    private static readonly string[] TextureGlobalWorldSplatMapNames =
    {
        "colossal_WorldSplatmap",
        "_COWorldSplatmap"
    };

    private static readonly string[] TextureGlobalSplatMapNames =
    {
        "colossal_Splatmap",
        "_COSplatmap"
    };

    private static readonly string[] TextureGlobalAmbientOcclusionNames =
    {
        "_AmbientOcclusionTexture",
        "_MultiAmbientOcclusionTexture",
        "_OcclusionTexture",
        "_AOPackedData",
        "_AOPackedBlurred",
        "_HiResAO",
        "_AoResult",
        "_AOOutputHistory"
    };

    private static readonly string[] KnownTextureProperties =
    {
        "Terrain SplatMap",
        "Splatmap",
        "FoliageCoverage",
        "Terrain HeightMap",
        "Heightmap",
        "Grass_BaseColorMap",
        "Grass_BaseColor",
        "Grass_AlbedoMap",
        "Grass_Albedo",
        "Grass_NormalMap",
        "Grass_Normal",
        "Grass_MaskMap",
        "Grass_Mask",
        "Foliage_BaseColorMap",
        "Foliage_BaseColor",
        "Foliage_AlbedoMap",
        "Foliage_Albedo",
        "Foliage_NormalMap",
        "Foliage_Normal",
        "Foliage_MaskMap",
        "Foliage_Mask",
        "BaseColorMap",
        "Base Color Map",
        "_BaseColorMap",
        "_BaseMap",
        "_MainTex",
        "MainTex",
        "Albedo",
        "NormalMap",
        "Normal Map",
        "_NormalMap",
        "Normal",
        "MaskMap",
        "_MaskMap"
    };

    private static readonly string[] KnownBoolProperties =
    {
        "Grass_Enabled",
        "Scatter1_Enabled",
        "DebugDistantGrass",
        "DebugGrassLOD"
    };

    private static readonly string[] KnownFloatProperties =
    {
        "Grass_Coverage",
        "FoliageCoverage",
        "FoliageDensity",
        "Grass_Density",
        "Grass_DensityMultiplier",
        "Density",
        "DensityMultiplier",
        "Grass_NormalScale",
        "Grass_NoiseScale",
        "Grass_QuadSize",
        "Grass_ScaleMultiplier",
        "Splatmap_SamplingScale",
        "Splatmap_WeightBlendStrength",
        "Scatter1_DensityMultiplier",
        "Scatter1_DistanceRangeMultiplier"
    };

    private static readonly string[] KnownVector2Properties =
    {
        "Crop Size"
    };

    private static readonly string[] KnownVector3Properties =
    {
        "CameraDirection",
        "CameraPosition",
        "TerrainBounds_center",
        "TerrainBounds_size",
        "Grass_Color",
        "Grass_Tint",
        "Foliage_Color",
        "Foliage_Tint",
        "BaseColor",
        "Tint",
        "_BaseColor",
        "_Color",
        "_TintColor"
    };

    private static readonly string[] KnownVector4Properties =
    {
        "Terrain Offset Scale",
        "Grass_Color",
        "Grass_Tint",
        "Grass_BaseColorTint",
        "Grass_AlbedoTint",
        "Foliage_Color",
        "Foliage_Tint",
        "Foliage_BaseColorTint",
        "Foliage_AlbedoTint",
        "BaseColor",
        "Base Color",
        "BaseColorFactor",
        "BaseColorTint",
        "AlbedoColor",
        "Tint",
        "TintColor",
        "_BaseColor",
        "_Color",
        "_TintColor"
    };

    private static readonly string[] KnownGradientProperties =
    {
        "Grass_ColorGradient",
        "Grass_Gradient",
        "Foliage_ColorGradient",
        "Foliage_Gradient",
        "ColorGradient",
        "BaseColorGradient",
        "Gradient"
    };

    private static readonly string[] KnownAnimationCurveProperties =
    {
        "Scale Over Distance"
    };

    private static readonly string[] DensityProperties =
    {
        "FoliageDensity",
        "Grass_Density",
        "Grass_DensityMultiplier",
        "GrassDensity",
        "Density",
        "DensityMultiplier",
        "SpawnRate",
        "Spawn Rate",
        "SpawnRateMultiplier"
    };

    private readonly string _rootPath;
    private readonly TextureLoader _textureLoader;
    private readonly ILog _log;
    private bool _loggedNoVfx;
    private bool _loggedDiagnostics;
    private bool _loggedForcedSystem;
    private bool _loggedUnsupportedDensity;
    private bool _loggedUnsupportedDistanceCurve;
    private bool _loggedUnsupportedFoliageBaseColor;
    private bool _loggedUnsupportedFoliageNormal;
    private bool _loggedUnsupportedFoliageMask;
    private bool _loggedUnsupportedFoliageColor;
    private bool _loggedUnsupportedParticleBudget;
    private bool _loggedLightingCorrection;
    private bool _loggedMissingLightingDirection;
    private bool _didForceVegetationSystem;
    private string _lastSummary = string.Empty;
    private string _lastParticleBudgetSummary = string.Empty;
    private string _lastLightingCorrectionSummary = string.Empty;
    private float _particleBudgetCoverageMultiplier = 1f;
    private float _nextFoliagePassLogTime;
    private float _nextParticleBudgetLogTime;
    private float _nextLightingCorrectionLogTime;
    private readonly Dictionary<string, Texture2D> _debugSplatMaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RenderTexture> _debugSplatRenderTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Color> _averageTextureColors = new();
    private RenderTexture? _ambientOcclusion2D;
    private RenderTexture? _ambientOcclusionDownsampled;
    private Texture2D? _ambientOcclusionGrowthMask;
    private float _nextAmbientOcclusionRefreshTime;
    private string _lastAmbientOcclusionSource = string.Empty;
    private readonly Dictionary<int, AppliedFoliageState> _appliedStates = new();
    private readonly HashSet<int> _playedEffects = new();
    private readonly List<VisualEffect> _cachedEffects = new();
    private float _nextEffectScanTime;
    private readonly FoliageRoadMaskGenerator _roadMaskGenerator;

    public FoliageVfxApplicator(string rootPath, TextureLoader textureLoader, ILog log)
    {
        _rootPath = rootPath;
        _textureLoader = textureLoader;
        _log = log;
        _roadMaskGenerator = new FoliageRoadMaskGenerator(log);
    }

    public void Apply(FoliageConfig config, World world)
    {
        if (!config.Enabled)
        {
            Restore(world);
            return;
        }

        if (config.ForceVegetationSystem)
        {
            ForceVegetationSystem(world);
        }

        var effects = GetFoliageEffects();
        if (effects.Count == 0)
        {
            if (!_loggedNoVfx)
            {
                _loggedNoVfx = true;
                _log.Info("Texture Unifier foliage pass: no active foliage/grass VisualEffect objects found yet.");
            }

            return;
        }

        FoliageTerrainBounds terrainBounds = ResolveTerrainBounds(effects, config);
        Texture? selectedSplatMap = ResolveSplatMap(config, world, terrainBounds, out string splatMapSource, out int splatMapRevision);
        int changedCount = 0;
        var changedNames = new List<string>();

        foreach (var effect in effects)
        {
            EnsureEffectPlaying(effect, config);
            bool changed = ApplyEffect(effect, config, selectedSplatMap, terrainBounds, splatMapRevision);
            if (!changed)
            {
                continue;
            }

            changedCount++;
            if (changedNames.Count < 8)
            {
                changedNames.Add(GetEffectLabel(effect));
            }
        }

        float now = Time.unscaledTime;
        string summaryKey = $"effects:{effects.Count}, splatMap:{splatMapSource}";
        string summary = $"effects:{effects.Count}, changed:{changedCount}, splatMap:{splatMapSource}";
        if (!string.Equals(summaryKey, _lastSummary, StringComparison.Ordinal) || now >= _nextFoliagePassLogTime)
        {
            _lastSummary = summaryKey;
            _nextFoliagePassLogTime = now + 5f;
            _log.Info($"Texture Unifier foliage pass: {summary}");
            if (changedNames.Count > 0)
            {
                _log.Info($"Texture Unifier foliage matched: {string.Join(" | ", changedNames)}");
                _log.Info($"Texture Unifier foliage state: {string.Join(" | ", effects.Take(8).Select(GetEffectStateLabel))}");
            }
        }

        if (config.LogDiagnostics && !_loggedDiagnostics)
        {
            _loggedDiagnostics = true;
            LogDiagnostics(effects, selectedSplatMap, splatMapSource);
        }
    }

    public void Restore(World world)
    {
        if (_didForceVegetationSystem)
        {
            try
            {
                var system = world.GetExistingSystemManaged<VegetationRenderSystem>();
                if (system != null)
                {
                    system.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Texture Unifier could not disable forced foliage system: {ex.Message}");
            }
        }

        _didForceVegetationSystem = false;
        _loggedForcedSystem = false;
        _lastSummary = string.Empty;
        _nextFoliagePassLogTime = 0f;
        _particleBudgetCoverageMultiplier = 1f;
        _lastParticleBudgetSummary = string.Empty;
        _loggedLightingCorrection = false;
        _loggedMissingLightingDirection = false;
        _lastLightingCorrectionSummary = string.Empty;
        _nextLightingCorrectionLogTime = 0f;
        _playedEffects.Clear();
        _appliedStates.Clear();
        _cachedEffects.Clear();
        _nextEffectScanTime = 0f;
        _roadMaskGenerator.Reset();
    }

    public void Dispose()
    {
        _roadMaskGenerator.Dispose();
        if (_ambientOcclusion2D != null)
        {
            _ambientOcclusion2D.Release();
            UnityEngine.Object.Destroy(_ambientOcclusion2D);
            _ambientOcclusion2D = null;
        }

        if (_ambientOcclusionDownsampled != null)
        {
            _ambientOcclusionDownsampled.Release();
            UnityEngine.Object.Destroy(_ambientOcclusionDownsampled);
            _ambientOcclusionDownsampled = null;
        }

        if (_ambientOcclusionGrowthMask != null)
        {
            UnityEngine.Object.Destroy(_ambientOcclusionGrowthMask);
            _ambientOcclusionGrowthMask = null;
        }
    }

    private void ForceVegetationSystem(World world)
    {
        try
        {
            var system = world.GetExistingSystemManaged<VegetationRenderSystem>();
            if (system == null)
            {
                _log.Warn("Texture Unifier foliage pass could not find Game.Rendering.VegetationRenderSystem.");
                return;
            }

            if (!system.Enabled)
            {
                system.Enabled = true;
            }

            _didForceVegetationSystem = true;

            if (!_loggedForcedSystem)
            {
                _loggedForcedSystem = true;
                _log.Info("Texture Unifier foliage pass forced VegetationRenderSystem enabled.");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Texture Unifier could not force VegetationRenderSystem enabled: {ex.Message}");
        }
    }

    private bool ApplyEffect(VisualEffect effect, FoliageConfig config, Texture? selectedSplatMap, FoliageTerrainBounds terrainBounds, int splatMapRevision)
    {
        bool changed = false;

        if (selectedSplatMap != null && config.SetTerrainSplatMap)
        {
            changed |= SetTextureIfExists(effect, TerrainSplatMapProperties, selectedSplatMap);
        }

        if (selectedSplatMap != null && config.SetFoliageCoverage)
        {
            changed |= SetTextureIfExists(effect, FoliageCoverageProperties, selectedSplatMap);
        }

        if (config.Texture.Enabled)
        {
            changed |= ApplyFoliageTextures(effect, config.Texture);
        }

        if (config.GrassEnabled.HasValue)
        {
            changed |= SetBoolIfExists(effect, "Grass_Enabled", config.GrassEnabled.Value);
        }

        bool hasDirectDensity = config.FoliageDensity.HasValue && HasFloat(effect, DensityProperties);
        bool coverageChanged = false;
        float? appliedCoverage = null;
        if (config.FoliageCoverage.HasValue)
        {
            float coverage = Mathf.Max(0f, config.FoliageCoverage.Value);
            coverage = ApplyParticleBudget(effect, config, coverage);
            appliedCoverage = coverage;
            coverageChanged = SetFloatIfExists(effect, "FoliageCoverage", coverage);
            changed |= coverageChanged;
        }

        if (config.FoliageDensity.HasValue)
        {
            bool densityChanged = hasDirectDensity && SetFloatIfExists(effect, DensityProperties, config.FoliageDensity.Value);
            if (!densityChanged && !_loggedUnsupportedDensity)
            {
                _loggedUnsupportedDensity = true;
                _log.Info("Texture Unifier foliage density: no exposed density/spawn-rate float found on the active FoliageVFX graph.");
            }

            changed |= densityChanged;
        }

        if (config.FoliageRenderDistance.HasValue)
        {
            bool distanceChanged = SetScaleOverDistanceIfExists(effect, config);
            if (!distanceChanged && !_loggedUnsupportedDistanceCurve)
            {
                _loggedUnsupportedDistanceCurve = true;
                _log.Info("Texture Unifier foliage render distance: active FoliageVFX has no exposed 'Scale Over Distance' curve.");
            }

            changed |= distanceChanged;
        }

        if (config.ScatterEnabled.HasValue)
        {
            changed |= SetBoolIfExists(effect, "Scatter1_Enabled", config.ScatterEnabled.Value);
        }

        if (config.GrassCoverage.HasValue)
        {
            changed |= SetFloatIfExists(effect, "Grass_Coverage", config.GrassCoverage.Value);
        }

        if (config.GrassNormalScale.HasValue)
        {
            changed |= SetFloatIfExists(effect, "Grass_NormalScale", config.GrassNormalScale.Value);
        }

        if (config.SplatmapSamplingScale.HasValue)
        {
            changed |= SetFloatIfExists(effect, "Splatmap_SamplingScale", config.SplatmapSamplingScale.Value);
        }

        if (config.SplatmapWeightBlendStrength.HasValue)
        {
            changed |= SetFloatIfExists(effect, "Splatmap_WeightBlendStrength", config.SplatmapWeightBlendStrength.Value);
        }

        if (config.GrassSplatIndexes.Length > 0)
        {
            changed |= SetGrassSplatIndexes(effect, config.GrassSplatIndexes);
        }

        Vector2 cropSize = ResolveCropSize(config, terrainBounds);
        changed |= SetVector2IfExists(effect, "Crop Size", cropSize);

        if (TryGetVector4(config.TerrainOffsetScale, out var terrainOffsetScale))
        {
            changed |= SetVector4IfExists(effect, "Terrain Offset Scale", terrainOffsetScale);
        }

        changed |= ApplyLightingDirectionCorrection(effect, config);

        ReinitializeIfNeeded(effect, appliedCoverage, cropSize, selectedSplatMap, splatMapRevision);

        return changed;
    }

    private bool ApplyLightingDirectionCorrection(VisualEffect effect, FoliageConfig config)
    {
        const string propertyName = "CameraDirection";
        if (!config.LockLightingToAwayFromSun || !effect.HasVector3(propertyName))
        {
            return false;
        }

        if (!TryResolveLightingDirection(config, out Vector3 direction, out string source))
        {
            if (!_loggedMissingLightingDirection)
            {
                _loggedMissingLightingDirection = true;
                _log.Info("Texture Unifier foliage lighting: could not resolve a sun direction for CameraDirection correction.");
            }

            return false;
        }

        bool changed = SetVector3IfExists(effect, propertyName, direction);
        string summary = $"{source}:{FormatVector3(direction)}";
        float now = Time.unscaledTime;
        if (!_loggedLightingCorrection ||
            (now >= _nextLightingCorrectionLogTime && !string.Equals(summary, _lastLightingCorrectionSummary, StringComparison.Ordinal)))
        {
            _loggedLightingCorrection = true;
            _lastLightingCorrectionSummary = summary;
            _nextLightingCorrectionLogTime = now + 30f;
            _log.Info($"Texture Unifier foliage lighting: locked CameraDirection to away-from-sun target ({summary}).");
        }

        return changed;
    }

    private bool ApplyFoliageTextures(VisualEffect effect, FoliageTextureConfig config)
    {
        bool changed = false;
        Texture2D? baseColorTexture = null;

        if (!string.IsNullOrWhiteSpace(config.BaseColor))
        {
            var texture = _textureLoader.LoadTexture(_rootPath, config.BaseColor, linear: false);
            if (texture != null)
            {
                baseColorTexture = texture;
                bool applied = SetTextureIfExists(effect, config.BaseColorShaderProperties, texture);
                if (!applied && !_loggedUnsupportedFoliageBaseColor)
                {
                    _loggedUnsupportedFoliageBaseColor = true;
                    _log.Info($"Texture Unifier foliage texture: no exposed base-color texture property found on the active FoliageVFX graph. Tried: {string.Join(", ", config.BaseColorShaderProperties)}. Exposed: {GetExposedPropertiesSummary(effect)}");
                }

                changed |= applied;
            }
        }

        if (config.MatchTerrainColor && baseColorTexture != null && TryGetAverageColor(baseColorTexture, out Color averageColor))
        {
            changed |= ApplyFoliageColor(effect, config, averageColor);
        }

        if (!string.IsNullOrWhiteSpace(config.Normal))
        {
            var texture = _textureLoader.LoadTexture(_rootPath, config.Normal, linear: true, normalStrength: config.NormalStrength);
            if (texture != null)
            {
                bool applied = SetTextureIfExists(effect, config.NormalShaderProperties, texture);
                if (!applied && !_loggedUnsupportedFoliageNormal)
                {
                    _loggedUnsupportedFoliageNormal = true;
                    _log.Info($"Texture Unifier foliage texture: no exposed normal texture property found on the active FoliageVFX graph. Tried: {string.Join(", ", config.NormalShaderProperties)}. Exposed: {GetExposedPropertiesSummary(effect)}");
                }

                changed |= applied;
            }
        }

        if (!string.IsNullOrWhiteSpace(config.Mask))
        {
            var texture = _textureLoader.LoadTexture(_rootPath, config.Mask, linear: true);
            if (texture != null)
            {
                bool applied = SetTextureIfExists(effect, config.MaskShaderProperties, texture);
                if (!applied && !_loggedUnsupportedFoliageMask)
                {
                    _loggedUnsupportedFoliageMask = true;
                    _log.Info($"Texture Unifier foliage texture: no exposed mask texture property found on the active FoliageVFX graph. Tried: {string.Join(", ", config.MaskShaderProperties)}. Exposed: {GetExposedPropertiesSummary(effect)}");
                }

                changed |= applied;
            }
        }

        return changed;
    }

    private bool ApplyFoliageColor(VisualEffect effect, FoliageTextureConfig config, Color averageColor)
    {
        Color tint = Color.Lerp(Color.white, new Color(averageColor.r, averageColor.g, averageColor.b, 1f), config.ColorMatchStrength);
        var tint3 = new Vector3(tint.r, tint.g, tint.b);
        var tint4 = new Vector4(tint.r, tint.g, tint.b, tint.a);
        bool changed = false;

        foreach (string propertyName in config.ColorShaderProperties.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (effect.HasVector4(propertyName))
            {
                effect.SetVector4(propertyName, tint4);
                changed = true;
            }

            if (effect.HasVector3(propertyName))
            {
                effect.SetVector3(propertyName, tint3);
                changed = true;
            }
        }

        Gradient? solidGradient = null;
        foreach (string propertyName in config.GradientShaderProperties.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!effect.HasGradient(propertyName))
            {
                continue;
            }

            solidGradient ??= CreateSolidGradient(tint);
            effect.SetGradient(propertyName, solidGradient);
            changed = true;
        }

        if (!changed && !_loggedUnsupportedFoliageColor)
        {
            _loggedUnsupportedFoliageColor = true;
            _log.Info($"Texture Unifier foliage color: no exposed color/tint property found on the active FoliageVFX graph. Tried vectors: {string.Join(", ", config.ColorShaderProperties)}; gradients: {string.Join(", ", config.GradientShaderProperties)}. Exposed: {GetExposedPropertiesSummary(effect)}");
        }

        return changed;
    }

    private void EnsureEffectPlaying(VisualEffect effect, FoliageConfig config)
    {
        if (!config.ForceVfxPlay)
        {
            return;
        }

        try
        {
            if (effect.gameObject != null && !effect.gameObject.activeSelf)
            {
                effect.gameObject.SetActive(true);
            }

            if (!effect.enabled)
            {
                effect.enabled = true;
            }

            effect.pause = false;
            effect.playRate = 1f;

            if (_playedEffects.Add(effect.GetInstanceID()))
            {
                effect.Reinit();
                effect.Play();
                _log.Info($"Texture Unifier foliage VFX play/reinit: {GetEffectStateLabel(effect)}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Texture Unifier could not force foliage VFX playing: {ex.Message}");
        }
    }

    private void ReinitializeIfNeeded(VisualEffect effect, float? coverage, Vector2 cropSize, Texture? splatMap, int splatMapRevision)
    {
        int id = effect.GetInstanceID();
        var next = new AppliedFoliageState(
            coverage ?? -1f,
            cropSize,
            splatMap != null ? splatMap.GetInstanceID() : 0,
            splatMapRevision);

        if (!_appliedStates.TryGetValue(id, out AppliedFoliageState previous))
        {
            _appliedStates[id] = next;
            ReinitializeEffect(effect, "initial foliage override");
            return;
        }

        string? reason = null;
        if (previous.Coverage <= 0.0001f && next.Coverage > 0.0001f)
        {
            reason = "coverage returned above zero";
        }
        else if (previous.SplatMapId != next.SplatMapId)
        {
            reason = "foliage mask texture changed";
        }
        else if (previous.SplatMapRevision != next.SplatMapRevision)
        {
            reason = "foliage mask content changed";
        }
        else if (Mathf.Abs(previous.Coverage - next.Coverage) > Mathf.Max(1f, Mathf.Abs(previous.Coverage) * 0.05f))
        {
            reason = "foliage coverage changed";
        }
        else if ((previous.CropSize - next.CropSize).sqrMagnitude > 1f)
        {
            reason = "foliage crop size changed";
        }

        _appliedStates[id] = next;
        if (reason == null)
        {
            return;
        }

        ReinitializeEffect(effect, reason);
    }

    private void ReinitializeEffect(VisualEffect effect, string reason)
    {
        try
        {
            effect.pause = false;
            effect.Reinit();
            effect.Play();
            _log.Info($"Texture Unifier foliage VFX reinit: {reason}; {GetEffectStateLabel(effect)}");
        }
        catch (Exception ex)
        {
            _log.Warn($"Texture Unifier could not reinitialize foliage VFX after {reason}: {ex.Message}");
        }
    }

    private static bool SetTextureIfExists(VisualEffect effect, IEnumerable<string> propertyNames, Texture texture)
    {
        bool changed = false;
        foreach (string propertyName in propertyNames)
        {
            if (!effect.HasTexture(propertyName))
            {
                continue;
            }

            try
            {
                if (ReferenceEquals(effect.GetTexture(propertyName), texture))
                {
                    continue;
                }
            }
            catch
            {
            }

            effect.SetTexture(propertyName, texture);
            changed = true;
        }

        return changed;
    }

    private static bool SetBoolIfExists(VisualEffect effect, string propertyName, bool value)
    {
        if (!effect.HasBool(propertyName))
        {
            return false;
        }

        try
        {
            if (effect.GetBool(propertyName) == value)
            {
                return false;
            }
        }
        catch
        {
        }

        effect.SetBool(propertyName, value);
        return true;
    }

    private static bool SetFloatIfExists(VisualEffect effect, string propertyName, float value)
    {
        if (!effect.HasFloat(propertyName))
        {
            return false;
        }

        try
        {
            float current = effect.GetFloat(propertyName);
            if (Mathf.Abs(current - value) <= Mathf.Max(0.0001f, Mathf.Abs(value) * 0.0001f))
            {
                return false;
            }
        }
        catch
        {
        }

        effect.SetFloat(propertyName, value);
        return true;
    }

    private static bool SetFloatIfExists(VisualEffect effect, IEnumerable<string> propertyNames, float value)
    {
        bool changed = false;
        foreach (string propertyName in propertyNames)
        {
            changed |= SetFloatIfExists(effect, propertyName, value);
        }

        return changed;
    }

    private static bool HasFloat(VisualEffect effect, IEnumerable<string> propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (effect.HasFloat(propertyName))
            {
                return true;
            }
        }

        return false;
    }

    private float ApplyParticleBudget(VisualEffect effect, FoliageConfig config, float coverage)
    {
        if (!config.FoliageAutoParticleBudget || config.FoliageParticleBudget <= 0 || coverage <= 0f)
        {
            _particleBudgetCoverageMultiplier = 1f;
            return coverage;
        }

        if (!TryGetParticleBudgetInfo(effect, out long alive, out long capacity, out string source))
        {
            if (!_loggedUnsupportedParticleBudget)
            {
                _loggedUnsupportedParticleBudget = true;
                _log.Info("Texture Unifier foliage particle budget: active FoliageVFX did not report live particle counts.");
            }

            return coverage;
        }

        float budget = Mathf.Clamp(config.FoliageParticleBudget, 10000, 1000000);
        if (capacity > 0)
        {
            budget = Mathf.Min(budget, capacity * 0.92f);
        }

        if (alive >= budget)
        {
            float ratio = alive / Mathf.Max(1f, budget);
            float growth = Mathf.Clamp(ratio * 1.08f, 1.04f, 2f);
            _particleBudgetCoverageMultiplier = Mathf.Clamp(_particleBudgetCoverageMultiplier * growth, 1f, 16f);
        }
        else if (alive < budget * 0.55f && _particleBudgetCoverageMultiplier > 1f)
        {
            _particleBudgetCoverageMultiplier = Mathf.Max(1f, _particleBudgetCoverageMultiplier * 0.96f);
        }

        float maxCoverage = Mathf.Max(coverage, config.FoliageCoverage ?? coverage);
        float adjustedCoverage = Mathf.Min(maxCoverage, coverage * _particleBudgetCoverageMultiplier);
        if (Mathf.Approximately(adjustedCoverage, maxCoverage))
        {
            _particleBudgetCoverageMultiplier = Mathf.Min(_particleBudgetCoverageMultiplier, maxCoverage / Mathf.Max(coverage, 0.0001f));
        }

        string summary = $"source:{source}, alive:{alive}, capacity:{capacity}, budget:{FormatFloat(budget)}, coverageMultiplier:{FormatFloat(_particleBudgetCoverageMultiplier)}, coverage:{FormatFloat(coverage)}->{FormatFloat(adjustedCoverage)}, maxCoverage:{FormatFloat(maxCoverage)}";
        float now = Time.unscaledTime;
        if (now >= _nextParticleBudgetLogTime && !string.Equals(summary, _lastParticleBudgetSummary, StringComparison.Ordinal))
        {
            _nextParticleBudgetLogTime = now + 2f;
            _lastParticleBudgetSummary = summary;
            _log.Info($"Texture Unifier foliage particle budget: {summary}");
        }

        return adjustedCoverage;
    }

    private static Vector2 ResolveCropSize(FoliageConfig config, FoliageTerrainBounds terrainBounds)
    {
        Vector2 cropSize = default;
        if (TryGetVector2(config.CropSize, out var configuredCropSize))
        {
            cropSize = new Vector2(
                Mathf.Max(0f, configuredCropSize.x),
                Mathf.Max(0f, configuredCropSize.y));
        }

        if (config.FoliageRenderDistance.HasValue && config.FoliageRenderDistance.Value > 0f)
        {
            float renderDiameter = config.FoliageRenderDistance.Value * 2f;
            cropSize.x = Mathf.Max(cropSize.x, renderDiameter);
            cropSize.y = Mathf.Max(cropSize.y, renderDiameter);
        }

        if (config.CoverWholeMap)
        {
            cropSize.x = Mathf.Max(cropSize.x, terrainBounds.Size.x);
            cropSize.y = Mathf.Max(cropSize.y, terrainBounds.Size.z);
        }

        if (cropSize.x <= 0f || cropSize.y <= 0f)
        {
            cropSize = new Vector2(128f, 128f);
        }

        return cropSize;
    }

    private static bool TryGetParticleBudgetInfo(VisualEffect effect, out long alive, out long capacity, out string source)
    {
        alive = 0L;
        capacity = 0L;
        source = "effect";

        try
        {
            alive = effect.aliveParticleCount;
        }
        catch
        {
            alive = 0L;
        }

        try
        {
            var particleSystems = new List<string>();
            effect.GetParticleSystemNames(particleSystems);
            foreach (string particleSystem in particleSystems)
            {
                var info = effect.GetParticleSystemInfo(particleSystem);
                if (info.aliveCount > alive)
                {
                    alive = info.aliveCount;
                }

                if (info.capacity > capacity)
                {
                    capacity = info.capacity;
                }

                source = particleSystem;
            }
        }
        catch
        {
        }

        return alive > 0L || capacity > 0L;
    }

    private static bool SetScaleOverDistanceIfExists(VisualEffect effect, FoliageConfig config)
    {
        const string propertyName = "Scale Over Distance";
        if (!effect.HasAnimationCurve(propertyName) || !config.FoliageRenderDistance.HasValue)
        {
            return false;
        }

        float distance = config.FoliageRenderDistance.Value;
        float fadeStart = config.FoliageFadeStartDistance ?? distance * 0.75f;
        fadeStart = Mathf.Clamp(fadeStart, 0f, distance);

        var curve = new AnimationCurve(
            new Keyframe(0f, config.FoliageNearScale),
            new Keyframe(fadeStart, config.FoliageNearScale),
            new Keyframe(distance, config.FoliageFarScale));

        for (int i = 0; i < curve.length; i++)
        {
            curve.SmoothTangents(i, 0f);
        }

        effect.SetAnimationCurve(propertyName, curve);
        return true;
    }

    private static bool SetVector2IfExists(VisualEffect effect, string propertyName, Vector2 value)
    {
        if (!effect.HasVector2(propertyName))
        {
            return false;
        }

        try
        {
            if ((effect.GetVector2(propertyName) - value).sqrMagnitude <= 0.0001f)
            {
                return false;
            }
        }
        catch
        {
        }

        effect.SetVector2(propertyName, value);
        return true;
    }

    private static bool SetVector3IfExists(VisualEffect effect, string propertyName, Vector3 value)
    {
        if (!effect.HasVector3(propertyName))
        {
            return false;
        }

        try
        {
            if ((effect.GetVector3(propertyName) - value).sqrMagnitude <= 0.0001f)
            {
                return false;
            }
        }
        catch
        {
        }

        effect.SetVector3(propertyName, value);
        return true;
    }

    private static bool SetVector4IfExists(VisualEffect effect, string propertyName, Vector4 value)
    {
        if (!effect.HasVector4(propertyName))
        {
            return false;
        }

        try
        {
            if ((effect.GetVector4(propertyName) - value).sqrMagnitude <= 0.0001f)
            {
                return false;
            }
        }
        catch
        {
        }

        effect.SetVector4(propertyName, value);
        return true;
    }

    private bool TryGetAverageColor(Texture2D texture, out Color color)
    {
        int instanceId = texture.GetInstanceID();
        if (_averageTextureColors.TryGetValue(instanceId, out color))
        {
            return true;
        }

        try
        {
            Color32[] pixels = texture.GetPixels32();
            if (pixels.Length == 0)
            {
                color = Color.white;
                return false;
            }

            int step = Math.Max(1, pixels.Length / 8192);
            double r = 0d;
            double g = 0d;
            double b = 0d;
            double weight = 0d;

            for (int i = 0; i < pixels.Length; i += step)
            {
                Color32 pixel = pixels[i];
                double alpha = pixel.a / 255d;
                if (alpha <= 0d)
                {
                    continue;
                }

                r += pixel.r * alpha;
                g += pixel.g * alpha;
                b += pixel.b * alpha;
                weight += alpha;
            }

            if (weight <= 0d)
            {
                color = Color.white;
                return false;
            }

            color = new Color(
                (float)(r / weight / 255d),
                (float)(g / weight / 255d),
                (float)(b / weight / 255d),
                1f);
            _averageTextureColors[instanceId] = color;
            return true;
        }
        catch (Exception ex)
        {
            color = Color.white;
            _log.Info($"Texture Unifier foliage color: could not sample {texture.name} average color ({ex.Message}).");
            return false;
        }
    }

    private static Gradient CreateSolidGradient(Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(color.a, 0f),
                new GradientAlphaKey(color.a, 1f)
            });
        return gradient;
    }

    private static bool TryGetVector2(float[]? values, out Vector2 vector)
    {
        vector = default;
        if (values == null || values.Length < 2)
        {
            return false;
        }

        vector = new Vector2(values[0], values[1]);
        return true;
    }

    private static bool TryResolveLightingDirection(FoliageConfig config, out Vector3 direction, out string source)
    {
        if (TryGetVector3(config.LightingDirectionOverride, out Vector3 configuredDirection) &&
            configuredDirection.sqrMagnitude > 0.0001f)
        {
            direction = configuredDirection.normalized;
            source = "config";
            return true;
        }

        Light sun = RenderSettings.sun;
        if (IsDirectionalLightUsable(sun))
        {
            direction = sun.transform.forward.normalized;
            source = $"RenderSettings.sun:{sun.name}";
            return true;
        }

        Light? bestLight = null;
        float bestScore = float.MinValue;
        foreach (var light in Resources.FindObjectsOfTypeAll<Light>())
        {
            if (!IsDirectionalLightUsable(light))
            {
                continue;
            }

            float score = Mathf.Max(0f, light.intensity);
            if (bestLight != null && score <= bestScore)
            {
                continue;
            }

            bestLight = light;
            bestScore = score;
        }

        if (bestLight != null)
        {
            direction = bestLight.transform.forward.normalized;
            source = $"directional light:{bestLight.name}";
            return true;
        }

        direction = default;
        source = "none";
        return false;
    }

    private static bool IsDirectionalLightUsable(Light? light)
    {
        return light != null &&
            light.type == UnityEngine.LightType.Directional &&
            light.enabled &&
            light.transform != null &&
            light.transform.forward.sqrMagnitude > 0.0001f;
    }

    private static bool TryGetVector3(float[]? values, out Vector3 vector)
    {
        vector = default;
        if (values == null || values.Length < 3)
        {
            return false;
        }

        vector = new Vector3(values[0], values[1], values[2]);
        return true;
    }

    private static bool TryGetVector4(float[]? values, out Vector4 vector)
    {
        vector = default;
        if (values == null || values.Length < 4)
        {
            return false;
        }

        vector = new Vector4(values[0], values[1], values[2], values[3]);
        return true;
    }

    private static bool SetGrassSplatIndexes(VisualEffect effect, int[] splatIndexes)
    {
        bool changed = false;
        for (int slot = 1; slot <= 10; slot++)
        {
            string propertyName = $"Grass_SplatIndex{slot}";
            int value = slot <= splatIndexes.Length ? splatIndexes[slot - 1] : -1;
            changed |= SetNumberIfExists(effect, propertyName, value);
        }

        return changed;
    }

    private static bool SetNumberIfExists(VisualEffect effect, string propertyName, int value)
    {
        if (effect.HasInt(propertyName))
        {
            effect.SetInt(propertyName, value);
            return true;
        }

        if (effect.HasUInt(propertyName))
        {
            effect.SetUInt(propertyName, value < 0 ? 0u : (uint)value);
            return true;
        }

        if (effect.HasFloat(propertyName))
        {
            effect.SetFloat(propertyName, value);
            return true;
        }

        return false;
    }

    private List<VisualEffect> GetFoliageEffects()
    {
        float now = Time.unscaledTime;
        _cachedEffects.RemoveAll(effect => effect == null);
        if (_cachedEffects.Count > 0 && now < _nextEffectScanTime)
        {
            return _cachedEffects;
        }

        _cachedEffects.Clear();
        _cachedEffects.AddRange(FindFoliageEffects());
        _nextEffectScanTime = now + (_cachedEffects.Count == 0 ? 1f : 5f);
        return _cachedEffects;
    }

    private static List<VisualEffect> FindFoliageEffects()
    {
        return Resources.FindObjectsOfTypeAll<VisualEffect>()
            .Where(effect => effect != null && ContainsAny(GetEffectSearchText(effect), FoliageNameTokens))
            .ToList();
    }

    private static string GetEffectSearchText(VisualEffect effect)
    {
        string assetName = effect.visualEffectAsset != null ? effect.visualEffectAsset.name : string.Empty;
        string objectName = effect.name ?? string.Empty;
        string gameObjectName = effect.gameObject != null ? effect.gameObject.name : string.Empty;
        return $"{objectName} {gameObjectName} {assetName}";
    }

    private Texture? ResolveSplatMap(FoliageConfig config, World world, FoliageTerrainBounds terrainBounds, out string source, out int revision)
    {
        Texture? configuredSplatMap = ResolveConfiguredSplatMap(config, world, terrainBounds, out string configuredSource, out int configuredRevision);
        Texture? debugSplatMap = ResolveDebugSplatMap(config.DebugSplatMapOverride, configuredSplatMap, out string debugSource);
        if (debugSplatMap != null)
        {
            source = $"{debugSource}, reference:{configuredSource}";
            revision = 0;
            return debugSplatMap;
        }

        source = configuredSource;
        revision = configuredRevision;
        return configuredSplatMap;
    }

    private Texture? ResolveConfiguredSplatMap(FoliageConfig config, World world, FoliageTerrainBounds terrainBounds, out string source, out int revision)
    {
        revision = 0;
        string normalized = NormalizeSource(config.SplatMapSource);
        if (normalized == "none" || normalized == "default")
        {
            source = "default";
            return null;
        }

        if (normalized == "roadmask" || normalized == "roadcarveout" || normalized == "splatmaproadmask")
        {
            Texture? baseSplatMap = ResolveBaseSplatMap(world, out string baseSource);
            Texture? roadMask = _roadMaskGenerator.GetOrUpdate(config, world, baseSplatMap, terrainBounds, out string roadMaskSource, out revision);
            source = $"{roadMaskSource}, baseSource:{baseSource}";
            return roadMask;
        }

        if (normalized == "roadmaskambientocclusion" || normalized == "roadmaskao" || normalized == "aoroadmask")
        {
            Texture? ambientOcclusion = ResolveAmbientOcclusionMap(config, out string aoSource);
            Texture? baseSplatMap = ambientOcclusion ?? ResolveBaseSplatMap(world, out aoSource);
            Texture? roadMask = _roadMaskGenerator.GetOrUpdate(config, world, baseSplatMap, terrainBounds, out string roadMaskSource, out revision);
            source = $"{roadMaskSource}, baseSource:{aoSource}";
            return roadMask;
        }

        if (normalized == "ambientocclusion" || normalized == "ao" || normalized == "ssao" || normalized == "screenspaceambientocclusion")
        {
            return ResolveAmbientOcclusionMap(config, out source);
        }

        if (normalized == "splatmap" || normalized == "terrain")
        {
            return ResolveBaseSplatMap(world, out source);
        }

        var worldSplat = TryGetGlobalTexture(TextureGlobalWorldSplatMapNames, out string worldSplatName);
        if (worldSplat != null)
        {
            source = $"{worldSplatName}={DescribeTexture(worldSplat)}";
            return worldSplat;
        }

        var fallbackSplat = TryGetGlobalTexture(TextureGlobalSplatMapNames, out string fallbackSplatName);
        if (fallbackSplat != null)
        {
            source = $"fallback:{fallbackSplatName}={DescribeTexture(fallbackSplat)}";
            return fallbackSplat;
        }

        source = "worldSplatmap:null";
        return null;
    }

    private Texture? ResolveBaseSplatMap(World world, out string source)
    {
        var terrainSystemSplat = TryGetTerrainMaterialSystemSplatMap(world);
        if (terrainSystemSplat != null)
        {
            source = $"TerrainMaterialSystem.splatmap={DescribeTexture(terrainSystemSplat)}";
            return terrainSystemSplat;
        }

        var globalSplat = TryGetGlobalTexture(TextureGlobalSplatMapNames, out string globalSplatName);
        source = globalSplat != null ? $"{globalSplatName}={DescribeTexture(globalSplat)}" : "splatmap:null";
        return globalSplat;
    }

    private Texture? ResolveAmbientOcclusionMap(FoliageConfig config, out string source)
    {
        float now = Time.unscaledTime;
        if (_ambientOcclusionGrowthMask != null && now < _nextAmbientOcclusionRefreshTime)
        {
            source = $"{_lastAmbientOcclusionSource}, cached";
            return _ambientOcclusionGrowthMask;
        }

        Texture? ambientOcclusion = TryGetGlobalTexture(TextureGlobalAmbientOcclusionNames, out string matchedName);
        if (ambientOcclusion == null)
        {
            source = $"screenSpaceAO:null ({string.Join("/", TextureGlobalAmbientOcclusionNames)})";
            return null;
        }

        if (IsTexture2DCompatible(ambientOcclusion))
        {
            Texture? inverted = TryConvertAmbientOcclusionToGrowthMask(ambientOcclusion, out string texture2DConversion);
            source = inverted != null
                ? $"screenSpaceAO:{matchedName}={DescribeTexture(ambientOcclusion)}, growthMask={DescribeTexture(inverted)}, {texture2DConversion}"
                : $"screenSpaceAO:{matchedName}={DescribeTexture(ambientOcclusion)}, unsupported:{texture2DConversion}";
            CacheAmbientOcclusionSource(source, config);
            return inverted;
        }

        Texture? converted = TryConvertAmbientOcclusionToGrowthMask(ambientOcclusion, out string arrayConversion);
        source = converted != null
            ? $"screenSpaceAO:{matchedName}={DescribeTexture(ambientOcclusion)}, growthMask={DescribeTexture(converted)}, {arrayConversion}"
            : $"screenSpaceAO:{matchedName}={DescribeTexture(ambientOcclusion)}, unsupported:{arrayConversion}";
        CacheAmbientOcclusionSource(source, config);
        return converted;
    }

    private static bool IsTexture2DCompatible(Texture texture)
    {
        return texture.dimension == TextureDimension.Tex2D;
    }

    private void CacheAmbientOcclusionSource(string source, FoliageConfig config)
    {
        _lastAmbientOcclusionSource = source;
        _nextAmbientOcclusionRefreshTime = Time.unscaledTime + Mathf.Max(0.25f, config.RoadMaskRefreshSeconds);
    }

    private Texture? TryConvertAmbientOcclusionToGrowthMask(Texture texture, out string conversion)
    {
        int width = Mathf.Max(1, texture.width);
        int height = Mathf.Max(1, texture.height);
        EnsureAmbientOcclusion2D(width, height);

        if (_ambientOcclusion2D == null)
        {
            conversion = "could not allocate 2D AO target";
            return null;
        }

        try
        {
            Graphics.CopyTexture(texture, 0, 0, _ambientOcclusion2D, 0, 0);
            return BuildAmbientOcclusionGrowthMask("copied screen-space texture slice 0", out conversion);
        }
        catch (Exception copyEx)
        {
            try
            {
                Graphics.Blit(texture, _ambientOcclusion2D);
                return BuildAmbientOcclusionGrowthMask($"blitted after CopyTexture failed ({copyEx.Message})", out conversion);
            }
            catch (Exception blitEx)
            {
                conversion = $"conversion failed: CopyTexture={copyEx.Message}; Blit={blitEx.Message}";
                return null;
            }
        }
    }

    private Texture? BuildAmbientOcclusionGrowthMask(string inputConversion, out string conversion)
    {
        if (_ambientOcclusion2D == null)
        {
            conversion = "missing AO source target";
            return null;
        }

        int size = Mathf.Clamp(Mathf.Max(_ambientOcclusion2D.width, _ambientOcclusion2D.height), 64, AmbientOcclusionMaskMaxResolution);
        EnsureAmbientOcclusionDownsampled(size, size);
        EnsureAmbientOcclusionGrowthMask(size, size);

        if (_ambientOcclusionDownsampled == null || _ambientOcclusionGrowthMask == null)
        {
            conversion = "could not allocate AO growth mask target";
            return null;
        }

        RenderTexture? previous = RenderTexture.active;
        try
        {
            Graphics.Blit(_ambientOcclusion2D, _ambientOcclusionDownsampled);
            RenderTexture.active = _ambientOcclusionDownsampled;
            _ambientOcclusionGrowthMask.ReadPixels(new Rect(0, 0, size, size), 0, 0, recalculateMipMaps: false);
            _ambientOcclusionGrowthMask.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            Color32[] pixels = _ambientOcclusionGrowthMask.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                float luminance = (pixels[i].r + pixels[i].g + pixels[i].b) / (3f * 255f);
                float growth = Mathf.Clamp01((1f - luminance) * AmbientOcclusionGrowthBoost);
                byte value = (byte)Mathf.Clamp(Mathf.RoundToInt(growth * 255f), 0, 255);
                pixels[i] = new Color32(value, value, value, 255);
            }

            _ambientOcclusionGrowthMask.SetPixels32(pixels);
            _ambientOcclusionGrowthMask.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            conversion = $"{inputConversion}, inverted/boosted dark AO into {size}x{size} growth mask";
            return _ambientOcclusionGrowthMask;
        }
        catch (Exception ex)
        {
            conversion = $"growth mask conversion failed: {ex.Message}";
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private void EnsureAmbientOcclusion2D(int width, int height)
    {
        if (_ambientOcclusion2D != null && _ambientOcclusion2D.width == width && _ambientOcclusion2D.height == height)
        {
            return;
        }

        if (_ambientOcclusion2D != null)
        {
            _ambientOcclusion2D.Release();
            UnityEngine.Object.Destroy(_ambientOcclusion2D);
        }

        _ambientOcclusion2D = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = $"TextureUnifier_ScreenSpaceAO2D_{width}x{height}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false
        };
        _ambientOcclusion2D.Create();
    }

    private void EnsureAmbientOcclusionDownsampled(int width, int height)
    {
        if (_ambientOcclusionDownsampled != null && _ambientOcclusionDownsampled.width == width && _ambientOcclusionDownsampled.height == height)
        {
            return;
        }

        if (_ambientOcclusionDownsampled != null)
        {
            _ambientOcclusionDownsampled.Release();
            UnityEngine.Object.Destroy(_ambientOcclusionDownsampled);
        }

        _ambientOcclusionDownsampled = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = $"TextureUnifier_ScreenSpaceAOGrowthRT_{width}x{height}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false
        };
        _ambientOcclusionDownsampled.Create();
    }

    private void EnsureAmbientOcclusionGrowthMask(int width, int height)
    {
        if (_ambientOcclusionGrowthMask != null && _ambientOcclusionGrowthMask.width == width && _ambientOcclusionGrowthMask.height == height)
        {
            return;
        }

        if (_ambientOcclusionGrowthMask != null)
        {
            UnityEngine.Object.Destroy(_ambientOcclusionGrowthMask);
        }

        _ambientOcclusionGrowthMask = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = $"TextureUnifier_ScreenSpaceAOGrowthMask_{width}x{height}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
    }

    private static FoliageTerrainBounds ResolveTerrainBounds(IReadOnlyList<VisualEffect> effects, FoliageConfig config)
    {
        VisualEffect? effect = effects.FirstOrDefault();
        if (effect != null && effect.HasVector3("TerrainBounds_center") && effect.HasVector3("TerrainBounds_size"))
        {
            Vector3 size = effect.GetVector3("TerrainBounds_size");
            if (size.x > 0f && size.z > 0f)
            {
                return new FoliageTerrainBounds(effect.GetVector3("TerrainBounds_center"), size);
            }
        }

        if (TryGetVector4(config.TerrainOffsetScale, out var offsetScale) && offsetScale.z > 0f && offsetScale.w > 0f)
        {
            return new FoliageTerrainBounds(Vector3.zero, new Vector3(offsetScale.z * 2f, 4096f, offsetScale.w * 2f));
        }

        return new FoliageTerrainBounds(Vector3.zero, new Vector3(14336f, 4096f, 14336f));
    }

    private Texture? ResolveDebugSplatMap(string debugMode, Texture? referenceTexture, out string source)
    {
        string normalized = NormalizeSource(debugMode);
        if (normalized == "none" || normalized == "off" || normalized == "false")
        {
            source = "debug:none";
            return null;
        }

        Color32[] colors = normalized switch
        {
            "black" => new[] { new Color32(0, 0, 0, 255) },
            "white" => new[] { new Color32(255, 255, 255, 255) },
            "red" => new[] { new Color32(255, 0, 0, 255) },
            "green" => new[] { new Color32(0, 255, 0, 255) },
            "blue" => new[] { new Color32(0, 0, 255, 255) },
            "alpha0" => new[] { new Color32(255, 255, 255, 0) },
            "checker" => new[] { new Color32(255, 255, 255, 255), new Color32(0, 0, 0, 255) },
            _ => Array.Empty<Color32>()
        };

        if (colors.Length == 0)
        {
            source = $"debug:{debugMode}:unknown";
            return null;
        }

        if (referenceTexture != null)
        {
            string key = $"{normalized}|{referenceTexture.width}x{referenceTexture.height}";
            if (!_debugSplatRenderTextures.TryGetValue(key, out var renderTexture) || renderTexture == null)
            {
                renderTexture = CreateDebugSplatRenderTexture(normalized, referenceTexture.width, referenceTexture.height, colors);
                _debugSplatRenderTextures[key] = renderTexture;
            }

            source = $"debug:{normalized}={DescribeTexture(renderTexture)}";
            return renderTexture;
        }

        if (!_debugSplatMaps.TryGetValue(normalized, out var texture) || texture == null)
        {
            texture = CreateDebugSplatMap(normalized, colors);
            _debugSplatMaps[normalized] = texture;
        }

        source = $"debug:{normalized}={DescribeTexture(texture)}";
        return texture;
    }

    private static RenderTexture CreateDebugSplatRenderTexture(string normalized, int width, int height, Color32[] colors)
    {
        var renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = $"TextureUnifier_FoliageDebugSplatMap_{normalized}_{width}x{height}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
            useMipMap = false,
            autoGenerateMips = false
        };

        renderTexture.Create();
        ClearDebugRenderTexture(renderTexture, colors);
        return renderTexture;
    }

    private static void ClearDebugRenderTexture(RenderTexture renderTexture, Color32[] colors)
    {
        RenderTexture? previous = RenderTexture.active;
        RenderTexture.active = renderTexture;

        if (colors.Length == 1)
        {
            GL.Clear(clearDepth: false, clearColor: true, backgroundColor: colors[0]);
        }
        else
        {
            var texture = CreateDebugSplatMap("checker-source", colors);
            Graphics.Blit(texture, renderTexture);
            UnityEngine.Object.Destroy(texture);
        }

        RenderTexture.active = previous;
    }

    private static Texture2D CreateDebugSplatMap(string normalized, Color32[] colors)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = $"TextureUnifier_FoliageDebugSplatMap_{normalized}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = colors.Length == 1 ? 0 : ((x / 8 + y / 8) & 1);
                pixels[y * size + x] = colors[index];
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }

    private Texture? TryGetTerrainMaterialSystemSplatMap(World world)
    {
        try
        {
            return world.GetExistingSystemManaged<TerrainMaterialSystem>()?.splatmap;
        }
        catch
        {
            return null;
        }
    }

    private static Texture? TryGetGlobalTexture(IEnumerable<string> names, out string matchedName)
    {
        foreach (string name in names)
        {
            Texture texture = Shader.GetGlobalTexture(name);
            if (texture == null)
            {
                continue;
            }

            matchedName = name;
            return texture;
        }

        matchedName = string.Join("/", names);
        return null;
    }

    private void LogDiagnostics(List<VisualEffect> effects, Texture? selectedSplatMap, string splatMapSource)
    {
        _log.Info($"Texture Unifier foliage diagnostics selected splat map: {splatMapSource}");
        LogGlobalTexture("colossal_Splatmap");
        LogGlobalTexture("_COSplatmap");
        LogGlobalTexture("colossal_WorldSplatmap");
        LogGlobalTexture("_COWorldSplatmap");
        foreach (string aoName in TextureGlobalAmbientOcclusionNames)
        {
            LogGlobalTexture(aoName);
        }

        foreach (var effect in effects.Take(8))
        {
            var parts = new List<string>
            {
                GetEffectLabel(effect)
            };

            foreach (string propertyName in KnownTextureProperties)
            {
                if (effect.HasTexture(propertyName))
                {
                    parts.Add($"{propertyName}:Texture={DescribeTexture(effect.GetTexture(propertyName))}");
                }
            }

            foreach (string propertyName in KnownBoolProperties)
            {
                if (effect.HasBool(propertyName))
                {
                    parts.Add($"{propertyName}:Bool={effect.GetBool(propertyName)}");
                }
            }

            foreach (string propertyName in KnownFloatProperties)
            {
                if (effect.HasFloat(propertyName))
                {
                    parts.Add($"{propertyName}:Float={effect.GetFloat(propertyName):0.###}");
                }
            }

            foreach (string propertyName in KnownVector2Properties)
            {
                if (effect.HasVector2(propertyName))
                {
                    parts.Add($"{propertyName}:Vector2={FormatVector2(effect.GetVector2(propertyName))}");
                }
            }

            foreach (string propertyName in KnownVector3Properties)
            {
                if (effect.HasVector3(propertyName))
                {
                    parts.Add($"{propertyName}:Vector3={FormatVector3(effect.GetVector3(propertyName))}");
                }
            }

            foreach (string propertyName in KnownVector4Properties)
            {
                if (effect.HasVector4(propertyName))
                {
                    parts.Add($"{propertyName}:Vector4={FormatVector4(effect.GetVector4(propertyName))}");
                }
            }

            foreach (string propertyName in KnownGradientProperties)
            {
                if (effect.HasGradient(propertyName))
                {
                    parts.Add($"{propertyName}:Gradient");
                }
            }

            foreach (string propertyName in KnownAnimationCurveProperties)
            {
                if (effect.HasAnimationCurve(propertyName))
                {
                    parts.Add($"{propertyName}:AnimationCurve={FormatCurve(effect.GetAnimationCurve(propertyName))}");
                }
            }

            parts.Add($"selectedSplatMap={(selectedSplatMap != null ? selectedSplatMap.name : "null")}");
            parts.Add($"state={GetEffectStateLabel(effect)}");
            parts.Add($"systems={GetVfxNames(effect)}");
            parts.Add($"exposed={GetExposedPropertiesSummary(effect)}");

            _log.Info($"Texture Unifier foliage diagnostic: {string.Join(" | ", parts)}");
        }
    }

    private void LogGlobalTexture(string name)
    {
        try
        {
            _log.Info($"Texture Unifier foliage global texture {name}: {DescribeTexture(Shader.GetGlobalTexture(name))}");
        }
        catch (Exception ex)
        {
            _log.Info($"Texture Unifier foliage global texture {name}: unreadable ({ex.Message})");
        }
    }

    private static string GetExposedPropertiesSummary(VisualEffect effect)
    {
        if (effect.visualEffectAsset == null)
        {
            return "no asset";
        }

        try
        {
            var properties = new List<VFXExposedProperty>();
            effect.visualEffectAsset.GetExposedProperties(properties);
            return string.Join(", ", properties.Take(80).Select(property => $"{property.name}:{property.type.Name}"));
        }
        catch (Exception ex)
        {
            return $"unreadable ({ex.Message})";
        }
    }

    private static string GetVfxNames(VisualEffect effect)
    {
        try
        {
            var systems = new List<string>();
            var particleSystems = new List<string>();
            var spawnSystems = new List<string>();
            effect.GetSystemNames(systems);
            effect.GetParticleSystemNames(particleSystems);
            effect.GetSpawnSystemNames(spawnSystems);
            return $"system=[{string.Join(",", systems)}], particles=[{string.Join(",", particleSystems)}], spawns=[{string.Join(",", spawnSystems)}]";
        }
        catch (Exception ex)
        {
            return $"unreadable ({ex.Message})";
        }
    }

    private static string GetEffectLabel(VisualEffect effect)
    {
        string assetName = effect.visualEffectAsset != null ? effect.visualEffectAsset.name : "no asset";
        string objectName = effect.name ?? "unnamed";
        string gameObjectName = effect.gameObject != null ? effect.gameObject.name : "no gameObject";
        return $"{objectName} / {gameObjectName} [{assetName}]";
    }

    private static string GetEffectStateLabel(VisualEffect effect)
    {
        try
        {
            string active = effect.gameObject != null ? effect.gameObject.activeSelf.ToString() : "no gameObject";
            return $"{GetEffectLabel(effect)} enabled={effect.enabled} active={active} activeAndEnabled={effect.isActiveAndEnabled} pause={effect.pause} culled={effect.culled} alive={effect.aliveParticleCount} playRate={effect.playRate:0.###}";
        }
        catch (Exception ex)
        {
            return $"{GetEffectLabel(effect)} state unreadable ({ex.Message})";
        }
    }

    private static string NormalizeSource(string source)
    {
        return source.Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    private static string DescribeTexture(Texture? texture)
    {
        if (texture == null)
        {
            return "null";
        }

        string dimensions = $"{texture.width}x{texture.height}";
        if (texture is Texture2DArray textureArray)
        {
            dimensions += $"x{textureArray.depth}";
        }

        return $"{texture.name} ({texture.GetType().Name}, {dimensions}, dimension={texture.dimension})";
    }

    private static string FormatVector2(Vector2 vector)
    {
        return $"({FormatFloat(vector.x)}, {FormatFloat(vector.y)})";
    }

    internal static string FormatVector3ForLog(Vector3 vector)
    {
        return FormatVector3(vector);
    }

    private static string FormatVector3(Vector3 vector)
    {
        return $"({FormatFloat(vector.x)}, {FormatFloat(vector.y)}, {FormatFloat(vector.z)})";
    }

    private static string FormatVector4(Vector4 vector)
    {
        return $"({FormatFloat(vector.x)}, {FormatFloat(vector.y)}, {FormatFloat(vector.z)}, {FormatFloat(vector.w)})";
    }

    private static string FormatCurve(AnimationCurve curve)
    {
        if (curve == null)
        {
            return "null";
        }

        return string.Join(",", curve.keys.Select(key => $"({FormatFloat(key.time)}:{FormatFloat(key.value)})"));
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool ContainsAny(string text, IEnumerable<string> needles)
    {
        foreach (string needle in needles)
        {
            if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct AppliedFoliageState
    {
        public AppliedFoliageState(float coverage, Vector2 cropSize, int splatMapId, int splatMapRevision)
        {
            Coverage = coverage;
            CropSize = cropSize;
            SplatMapId = splatMapId;
            SplatMapRevision = splatMapRevision;
        }

        public float Coverage { get; }

        public Vector2 CropSize { get; }

        public int SplatMapId { get; }

        public int SplatMapRevision { get; }
    }
}
