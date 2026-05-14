using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace TextureUnifier;

internal sealed class MaterialTextureApplicator
{
    private const float RoadNonWorldspaceScaleMultiplier = 0.5f;

    private static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int WorldspaceAlbedo = Shader.PropertyToID("_WorldspaceAlbedo");
    private static readonly int NormalMap = Shader.PropertyToID("_NormalMap");
    private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
    private static readonly int WorldspaceNormalMap = Shader.PropertyToID("_WorldspaceNormalMap");
    private static readonly int DetailMap = Shader.PropertyToID("_DetailMap");
    private static readonly int DetailAlbedoMap = Shader.PropertyToID("_DetailAlbedoMap");
    private static readonly int DetailNormalMap = Shader.PropertyToID("_DetailNormalMap");
    private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
    private static readonly int WorldspaceUVScale = Shader.PropertyToID("_WorldspaceUVScale");

    private static readonly string[] RoadSurfaceExclusionOverrideKeywords =
    {
        "PavementSurface",
        "Pavement_Surface",
        "Pavement Surface",
        "Pavement01",
        "Pavement_01",
        "Pavement 01",
        "AsphaltSurface",
        "Asphalt_Surface",
        "Asphalt Surface",
        "SurfaceAsphalt",
        "Surface_Asphalt",
        "Surface Asphalt",
        "ParkingLane",
        "Parking Lane",
        "Parking_Lane",
        "ParkingRoad",
        "Parking Road",
        "RoadParking",
        "Road Parking",
        "RoadEUWorldspace_BaseColor",
        "RoadEUWorldspace",
        "RoadEU_BaseColor",
        "RoadEU",
        "Road_BaseColor",
        "Road_Normal",
        "_WorldspaceAlbedo",
        "_WorldspaceNormalMap"
    };

    private static readonly string[] AssetRoadSurfaceKeywords =
    {
        "PavementSurface",
        "Pavement_Surface",
        "Pavement Surface",
        "Pavement01",
        "Pavement_01",
        "Pavement 01",
        "AsphaltSurface",
        "Asphalt_Surface",
        "Asphalt Surface",
        "SurfaceAsphalt",
        "Surface_Asphalt",
        "Surface Asphalt"
    };

    private static readonly string[] AssetRoadSurfaceShaderKeywords =
    {
        "AreaDecalShader"
    };

    private static readonly string[] RoadMaterialShaderKeywords =
    {
        "NetCompositionMeshLitShader",
        "AreaDecalShader"
    };

    private static readonly string[] RoadMaterialIdentityKeywords =
    {
        "Road_",
        "TiledRoad",
        "RoadEUWorldspace",
        "RoadEU",
        "Road_BaseColor",
        "Road_Normal",
        "PavementSurface",
        "Pavement_Surface",
        "Pavement Surface",
        "Pavement01",
        "Pavement_01",
        "Pavement 01",
        "ParkingLane",
        "Parking Lane",
        "Parking_Lane",
        "ParkingRoad",
        "Parking Road",
        "RoadParking",
        "Road Parking",
        "AsphaltSurface",
        "Asphalt_Surface",
        "Asphalt Surface",
        "SurfaceAsphalt",
        "Surface_Asphalt",
        "Surface Asphalt"
    };

    private static readonly MaterialProperty[] BaseTextureProperties =
    {
        new(BaseColorMap, "_BaseColorMap"),
        new(MainTex, "_MainTex"),
        new(BaseMap, "_BaseMap")
    };

    private static readonly MaterialProperty[] NormalTextureProperties =
    {
        new(NormalMap, "_NormalMap"),
        new(BumpMap, "_BumpMap")
    };

    private static readonly MaterialProperty[] DetailTextureProperties =
    {
        new(DetailMap, "_DetailMap"),
        new(DetailAlbedoMap, "_DetailAlbedoMap"),
        new(DetailNormalMap, "_DetailNormalMap")
    };

    private readonly string _rootPath;
    private readonly TextureLoader _textureLoader;
    private readonly ILog _log;
    private readonly Dictionary<Material, MaterialSnapshot> _originals = new();
    private string _lastSummary = string.Empty;

    public MaterialTextureApplicator(string rootPath, TextureLoader textureLoader, ILog log)
    {
        _rootPath = rootPath;
        _textureLoader = textureLoader;
        _log = log;
    }

    public void Apply(NetworkTextureConfig config)
    {
        if (!config.Enabled)
        {
            Restore();
            return;
        }

        TextureSlotConfig[] slots =
        {
            config.Road,
            config.RoadWear,
            config.ParkingLot,
            config.Sidewalk,
            config.Gravel
        };

        var materials = Resources.FindObjectsOfTypeAll<Material>();
        var matchedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var changedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var changedNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var touchedMaterials = new HashSet<Material>();

        foreach (var material in materials)
        {
            if (material == null)
            {
                continue;
            }

            foreach (var slot in slots)
            {
                if (!slot.Enabled || !Matches(material, slot))
                {
                    continue;
                }

                matchedCounts.TryGetValue(slot.Name, out int matched);
                matchedCounts[slot.Name] = matched + 1;
                touchedMaterials.Add(material);

                if (ApplySlot(material, slot))
                {
                    changedCounts.TryGetValue(slot.Name, out int current);
                    changedCounts[slot.Name] = current + 1;

                    if (!changedNames.TryGetValue(slot.Name, out var names))
                    {
                        names = new List<string>();
                        changedNames[slot.Name] = names;
                    }

                    if (names.Count < 8)
                    {
                        names.Add(GetMaterialLabel(material));
                    }
                }

            }
        }

        foreach (var material in _originals.Keys.ToArray())
        {
            if (material == null || touchedMaterials.Contains(material))
            {
                continue;
            }

            _originals[material].Restore(material);
            _originals.Remove(material);
        }

        string matchedSummary = string.Join(", ", matchedCounts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
        string changedSummary = string.Join(", ", changedCounts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
        bool noMatches = matchedSummary.Length == 0;
        string summary = noMatches
            ? "no network material matches"
            : changedSummary.Length == 0
                ? $"{matchedSummary}; changed:none"
                : $"{matchedSummary}; changed:{changedSummary}";

        if (!string.Equals(summary, _lastSummary, StringComparison.Ordinal))
        {
            _lastSummary = summary;
            _log.Info($"Texture Unifier network pass: {summary}");
            foreach (var pair in changedNames.OrderBy(pair => pair.Key))
            {
                _log.Info($"Texture Unifier matched {pair.Key}: {string.Join(" | ", pair.Value)}");
            }

            if (noMatches)
            {
                var candidates = GetDiagnosticCandidates(materials);
                if (candidates.Count > 0)
                {
                    _log.Info($"Texture Unifier candidate network-ish materials: {string.Join(" | ", candidates)}");
                }

                LogShaderGlobals();
            }
        }
    }

    public void Restore()
    {
        RestoreAppliedMaterials(clearSummary: true);
    }

    private void RestoreAppliedMaterials(bool clearSummary)
    {
        foreach (var pair in _originals.ToArray())
        {
            if (pair.Key != null)
            {
                pair.Value.Restore(pair.Key);
            }
        }

        _originals.Clear();
        if (clearSummary)
        {
            _lastSummary = string.Empty;
        }
    }

    private bool ApplySlot(Material material, TextureSlotConfig slot)
    {
        HashSet<int> textureProperties = GetTextureProperties(material);
        bool suppressMissingTextureWarnings = IsOptionalAtlasSlot(slot);
        Texture2D? baseTexture = LoadSlotTexture(slot.BaseColor, linear: false, normalStrength: 1f, suppressMissingTextureWarning: suppressMissingTextureWarnings);
        Texture2D? normalTexture = LoadSlotTexture(slot.Normal, linear: true, normalStrength: slot.NormalStrength, suppressMissingTextureWarning: suppressMissingTextureWarnings);

        bool hasBase = baseTexture != null;
        bool hasNormal = normalTexture != null;
        float lowFrequencyScale = GetLowFrequencyScale(slot, textureProperties);
        bool hasScale = (hasBase || hasNormal) && (lowFrequencyScale > 0f || slot.HighFrequencyScale > 0f);
        bool hasSmoothness = slot.Smoothness.HasValue && material.HasProperty(Smoothness);
        bool hasWorldspaceScale = slot.WorldspaceUVScale.HasValue && material.HasProperty(WorldspaceUVScale);

        if (!hasBase && !hasNormal && !hasScale && !hasSmoothness && !hasWorldspaceScale)
        {
            return false;
        }

        if (!_originals.ContainsKey(material))
        {
            _originals[material] = MaterialSnapshot.Capture(material, textureProperties);
        }

        bool changed = false;
        if (baseTexture != null)
        {
            changed |= SetTexture(material, textureProperties, GetBaseTextureProperties(material, slot, textureProperties), baseTexture);
        }

        if (normalTexture != null)
        {
            changed |= SetTexture(material, textureProperties, GetNormalTextureProperties(material, slot, textureProperties), normalTexture);
        }

        if (lowFrequencyScale > 0f)
        {
            var scale = new Vector2(lowFrequencyScale, lowFrequencyScale);
            changed |= SetTextureScale(material, textureProperties, GetBaseTextureProperties(material, slot, textureProperties), scale);
            changed |= SetTextureScale(material, textureProperties, GetNormalTextureProperties(material, slot, textureProperties), scale);
        }

        if (slot.HighFrequencyScale > 0f)
        {
            var scale = new Vector2(slot.HighFrequencyScale, slot.HighFrequencyScale);
            changed |= SetTextureScale(material, textureProperties, DetailTextureProperties, scale);
        }

        if (hasSmoothness)
        {
            changed |= SetFloat(material, Smoothness, Mathf.Clamp01(slot.Smoothness!.Value));
        }

        if (hasWorldspaceScale)
        {
            changed |= SetFloat(material, WorldspaceUVScale, slot.WorldspaceUVScale!.Value);
        }

        return changed;
    }

    private Texture2D? LoadSlotTexture(string relativeOrAbsolutePath, bool linear, float normalStrength, bool suppressMissingTextureWarning)
    {
        if (suppressMissingTextureWarning && !TextureFileExists(relativeOrAbsolutePath))
        {
            return null;
        }

        return _textureLoader.LoadTexture(_rootPath, relativeOrAbsolutePath, linear, normalStrength);
    }

    private bool TextureFileExists(string relativeOrAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            return false;
        }

        string path = Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(_rootPath, relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(path);
    }

    private static bool SetTexture(Material material, HashSet<int> textureProperties, IEnumerable<MaterialProperty> properties, Texture texture)
    {
        bool changed = false;
        foreach (var property in properties)
        {
            if (textureProperties.Contains(property.Id) && material.GetTexture(property.Id) != texture)
            {
                material.SetTexture(property.Id, texture);
                changed = true;
            }
        }

        return changed;
    }

    private static bool SetTextureScale(Material material, HashSet<int> textureProperties, IEnumerable<MaterialProperty> properties, Vector2 scale)
    {
        bool changed = false;
        foreach (var property in properties)
        {
            if (textureProperties.Contains(property.Id) && material.GetTextureScale(property.Id) != scale)
            {
                material.SetTextureScale(property.Id, scale);
                changed = true;
            }
        }

        return changed;
    }

    private static bool SetFloat(Material material, int propertyId, float value)
    {
        if (!material.HasProperty(propertyId) || Mathf.Approximately(material.GetFloat(propertyId), value))
        {
            return false;
        }

        material.SetFloat(propertyId, value);
        return true;
    }

    private static float GetLowFrequencyScale(TextureSlotConfig slot, HashSet<int> textureProperties)
    {
        float scale = slot.LowFrequencyScale;
        if (scale > 0f && IsRoadSlot(slot) && !textureProperties.Contains(WorldspaceAlbedo))
        {
            return scale * RoadNonWorldspaceScaleMultiplier;
        }

        return scale;
    }

    private static bool Matches(Material material, TextureSlotConfig slot)
    {
        string shaderName = material.shader != null ? material.shader.name : string.Empty;
        bool shaderAllowed = slot.ShaderNameKeywords.Length == 0 || ContainsAny(shaderName, slot.ShaderNameKeywords);
        if (!shaderAllowed)
        {
            return false;
        }

        string materialText = BuildMaterialText(material);
        if (IsSidewalkSlot(slot) && IsAssetRoadSurface(materialText, shaderName))
        {
            return false;
        }

        if (IsRoadSlot(slot) && !IsRoadMaterialCandidate(materialText, shaderName))
        {
            return false;
        }

        bool matched = ContainsAny(material.name, slot.MaterialNameKeywords) ||
            ContainsAny(materialText, slot.TextureNameKeywords);
        if (!matched)
        {
            return false;
        }

        bool excluded = ContainsAny(materialText, slot.ExcludeNameKeywords);
        if (!excluded)
        {
            return true;
        }

        return IsRoadSlot(slot) && ContainsAny(materialText, RoadSurfaceExclusionOverrideKeywords);
    }

    private static bool IsRoadSlot(TextureSlotConfig slot) =>
        string.Equals(slot.Name, "road", StringComparison.OrdinalIgnoreCase);

    private static bool IsSidewalkSlot(TextureSlotConfig slot) =>
        string.Equals(slot.Name, "sidewalk", StringComparison.OrdinalIgnoreCase);

    private static bool IsOptionalAtlasSlot(TextureSlotConfig slot) =>
        string.Equals(slot.Name, "parkingLot", StringComparison.OrdinalIgnoreCase);

    private static bool IsRoadMaterialCandidate(string materialText, string shaderName) =>
        ContainsAny(shaderName, RoadMaterialShaderKeywords) ||
        ContainsAny(materialText, RoadMaterialIdentityKeywords);

    private static bool IsAssetRoadSurface(string materialText, string shaderName) =>
        ContainsAny(shaderName, AssetRoadSurfaceShaderKeywords) &&
        ContainsAny(materialText, AssetRoadSurfaceKeywords);

    private static string BuildMaterialText(Material material)
    {
        HashSet<int> textureProperties = GetTextureProperties(material);
        var parts = new List<string>
        {
            material.name ?? string.Empty,
            material.shader != null ? material.shader.name : string.Empty
        };

        try
        {
            foreach (string propertyName in material.GetTexturePropertyNames())
            {
                int propertyId = Shader.PropertyToID(propertyName);
                if (!textureProperties.Contains(propertyId))
                {
                    continue;
                }

                parts.Add(propertyName);
                var texture = material.GetTexture(propertyName);
                if (texture != null)
                {
                    parts.Add(texture.name ?? string.Empty);
                }
            }
        }
        catch
        {
            foreach (var property in BaseTextureProperties.Concat(NormalTextureProperties).Concat(DetailTextureProperties))
            {
                if (!textureProperties.Contains(property.Id))
                {
                    continue;
                }

                var texture = material.GetTexture(property.Id);
                if (texture != null)
                {
                    parts.Add(texture.name ?? string.Empty);
                }
            }
        }

        return string.Join(" ", parts);
    }

    private static string GetMaterialLabel(Material material)
    {
        string shaderName = material.shader != null ? material.shader.name : "no shader";
        var textureParts = new List<string>();

        try
        {
            foreach (string propertyName in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(propertyName);
                textureParts.Add($"{propertyName}={(texture != null ? texture.name : "null")}");
                if (textureParts.Count >= 8)
                {
                    break;
                }
            }
        }
        catch
        {
            textureParts.Add("textures unavailable");
        }

        var propertyParts = new List<string>();
        try
        {
            var shader = material.shader;
            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count && propertyParts.Count < 12; i++)
                {
                    string propertyName = shader.GetPropertyName(i);
                    ShaderPropertyType propertyType = shader.GetPropertyType(i);
                    propertyParts.Add(DescribeMaterialProperty(material, propertyName, propertyType));
                }
            }
        }
        catch
        {
            propertyParts.Add("shader props unavailable");
        }

        string textureText = textureParts.Count > 0 ? $" textures: {string.Join(", ", textureParts)}" : string.Empty;
        string propertyText = propertyParts.Count > 0 ? $" props: {string.Join(", ", propertyParts)}" : string.Empty;
        return $"{material.name} [{shaderName}]{textureText}{propertyText}";
    }

    private static string DescribeMaterialProperty(Material material, string propertyName, ShaderPropertyType propertyType)
    {
        try
        {
            return propertyType switch
            {
                ShaderPropertyType.Texture => $"{propertyName}:Texture={(material.GetTexture(propertyName) != null ? material.GetTexture(propertyName).name : "null")}",
                ShaderPropertyType.Color => $"{propertyName}:Color={material.GetColor(propertyName)}",
                ShaderPropertyType.Float => $"{propertyName}:Float={material.GetFloat(propertyName):0.###}",
                ShaderPropertyType.Range => $"{propertyName}:Range={material.GetFloat(propertyName):0.###}",
                ShaderPropertyType.Vector => $"{propertyName}:Vector={material.GetVector(propertyName)}",
                _ => $"{propertyName}:{propertyType}"
            };
        }
        catch
        {
            return $"{propertyName}:{propertyType}=unreadable";
        }
    }

    private static List<string> GetDiagnosticCandidates(IEnumerable<Material> materials)
    {
        var result = new List<string>();
        string[] tokens = { "Road", "Lane", "Asphalt", "Parking", "ParkingLot", "CarPark", "CarLane", "Sidewalk", "Pavement", "Pedestrian", "Gravel" };

        foreach (var material in materials)
        {
            if (material == null)
            {
                continue;
            }

            string text;
            try
            {
                text = BuildMaterialText(material);
            }
            catch
            {
                continue;
            }

            if (!ContainsAny(text, tokens))
            {
                continue;
            }

            result.Add(GetMaterialLabel(material));
            if (result.Count >= 20)
            {
                break;
            }
        }

        return result;
    }

    private void LogShaderGlobals()
    {
        string[] textureGlobals =
        {
            "colossal_TerrainTextureArray",
            "colossal_TerrainTexture",
            "colossal_TerrainDownScaledTexture",
            "colossal_TerrainTextureArrayBaseLod",
            "colossal_TerrainGrassDiffuse",
            "colossal_TerrainDirtDiffuse",
            "colossal_TerrainRockDiffuse",
            "colossal_OverlayCurveBuffer",
            "colossal_OverlayCustomMeshBuffer"
        };

        foreach (string globalName in textureGlobals)
        {
            try
            {
                Texture texture = Shader.GetGlobalTexture(globalName);
                _log.Info($"Texture Unifier global texture {globalName}: {DescribeTexture(texture)}");
            }
            catch (Exception ex)
            {
                _log.Info($"Texture Unifier global texture {globalName}: unreadable ({ex.Message})");
            }
        }

        string[] vectorGlobals =
        {
            "colossal_TerrainTextureTiling",
            "colossal_TerrainScale",
            "colossal_TerrainOffset",
            "colossal_TerrainCascadeLimit",
            "colossal_UVScale"
        };

        foreach (string globalName in vectorGlobals)
        {
            try
            {
                _log.Info($"Texture Unifier global vector {globalName}: {Shader.GetGlobalVector(globalName)}");
            }
            catch (Exception ex)
            {
                _log.Info($"Texture Unifier global vector {globalName}: unreadable ({ex.Message})");
            }
        }
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

    private static HashSet<int> GetTextureProperties(Material material)
    {
        try
        {
            return new HashSet<int>(material.GetTexturePropertyNameIDs());
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    private static IEnumerable<MaterialProperty> GetBaseTextureProperties(Material material, TextureSlotConfig slot, HashSet<int> textureProperties)
    {
        var properties = slot.BaseColorShaderProperties.Length > 0
            ? slot.BaseColorShaderProperties.Select(name => new MaterialProperty(Shader.PropertyToID(name), name))
            : BaseTextureProperties;

        return SelectRoadWorldspaceProperties(slot, textureProperties, properties, WorldspaceAlbedo);
    }

    private static IEnumerable<MaterialProperty> GetNormalTextureProperties(Material material, TextureSlotConfig slot, HashSet<int> textureProperties)
    {
        var properties = slot.NormalShaderProperties.Length > 0
            ? slot.NormalShaderProperties.Select(name => new MaterialProperty(Shader.PropertyToID(name), name))
            : NormalTextureProperties;

        return SelectRoadWorldspaceProperties(slot, textureProperties, properties, WorldspaceNormalMap);
    }

    private static IEnumerable<MaterialProperty> SelectRoadWorldspaceProperties(
        TextureSlotConfig slot,
        HashSet<int> textureProperties,
        IEnumerable<MaterialProperty> properties,
        int worldspacePropertyId)
    {
        var resolved = properties.ToArray();
        if (!IsRoadSlot(slot) || !textureProperties.Contains(worldspacePropertyId))
        {
            return resolved;
        }

        return resolved.Where(property => property.Id == worldspacePropertyId);
    }

    private static bool ContainsAny(string? text, string[] needles)
    {
        if (string.IsNullOrWhiteSpace(text) || needles.Length == 0)
        {
            return false;
        }

        foreach (string needle in needles)
        {
            if (!string.IsNullOrWhiteSpace(needle) &&
                text!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct MaterialProperty
    {
        public MaterialProperty(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }

        public string Name { get; }
    }

    private sealed class MaterialSnapshot
    {
        private readonly Dictionary<int, Texture?> _textures = new();
        private readonly Dictionary<int, Vector2> _scales = new();
        private readonly Dictionary<int, float> _floats = new();

        public static MaterialSnapshot Capture(Material material, HashSet<int> textureProperties)
        {
            var snapshot = new MaterialSnapshot();

            foreach (int texturePropertyId in textureProperties)
            {
                snapshot._textures[texturePropertyId] = material.GetTexture(texturePropertyId);
                snapshot._scales[texturePropertyId] = material.GetTextureScale(texturePropertyId);
            }

            if (material.HasProperty(Smoothness))
            {
                snapshot._floats[Smoothness] = material.GetFloat(Smoothness);
            }

            if (material.HasProperty(WorldspaceUVScale))
            {
                snapshot._floats[WorldspaceUVScale] = material.GetFloat(WorldspaceUVScale);
            }

            return snapshot;
        }

        public void Restore(Material material)
        {
            foreach (var pair in _textures)
            {
                if (material.HasProperty(pair.Key))
                {
                    material.SetTexture(pair.Key, pair.Value);
                }
            }

            foreach (var pair in _scales)
            {
                if (material.HasProperty(pair.Key))
                {
                    material.SetTextureScale(pair.Key, pair.Value);
                }
            }

            foreach (var pair in _floats)
            {
                if (material.HasProperty(pair.Key))
                {
                    material.SetFloat(pair.Key, pair.Value);
                }
            }
        }
    }
}
