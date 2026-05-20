using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TextureUnifier;

internal sealed class TextureUnifierConfig
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("autoReloadSeconds")]
    public float AutoReloadSeconds { get; set; } = 2f;

    [JsonProperty("terrainApplyIntervalSeconds")]
    public float TerrainApplyIntervalSeconds { get; set; } = 3f;

    [JsonProperty("activeTexturePack")]
    public string ActiveTexturePack { get; set; } = string.Empty;

    [JsonProperty("terrain")]
    public TerrainTextureConfig Terrain { get; set; } = TerrainTextureConfig.CreateDefault();

    [JsonProperty("networks")]
    public NetworkTextureConfig Networks { get; set; } = NetworkTextureConfig.CreateDefault();

    [JsonProperty("foliage")]
    public FoliageConfig Foliage { get; set; } = FoliageConfig.CreateDefault();

    public static TextureUnifierConfig LoadOrCreate(string configPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        if (!File.Exists(configPath))
        {
            var defaults = CreateDefault();
            File.WriteAllText(configPath, JsonConvert.SerializeObject(defaults, Formatting.Indented));
            return defaults;
        }

        var config = JsonConvert.DeserializeObject<TextureUnifierConfig>(File.ReadAllText(configPath)) ?? CreateDefault();
        config.Normalize();
        return config;
    }

    public void Save(string configPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        Normalize();
        string nextJson = JsonConvert.SerializeObject(this, Formatting.Indented);
        if (File.Exists(configPath) && string.Equals(File.ReadAllText(configPath), nextJson, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(configPath, nextJson);
    }

    internal static TextureUnifierConfig CreateDefault()
    {
        var config = new TextureUnifierConfig();
        config.Normalize();
        return config;
    }

    private void Normalize()
    {
        if (AutoReloadSeconds < 0.5f)
        {
            AutoReloadSeconds = 0.5f;
        }

        if (TerrainApplyIntervalSeconds < 1f)
        {
            TerrainApplyIntervalSeconds = 1f;
        }

        ActiveTexturePack ??= string.Empty;
        Terrain ??= TerrainTextureConfig.CreateDefault();
        Networks ??= NetworkTextureConfig.CreateDefault();
        Foliage ??= FoliageConfig.CreateDefault();
        Terrain.Normalize();
        Networks.Normalize();
        Foliage.Normalize();
        ApplySimpleOptionsProfile();
        if (!string.IsNullOrWhiteSpace(ActiveTexturePack))
        {
            TexturePackManager.ApplyPack(this, ActiveTexturePack);
        }

        SyncParkingLotTexturesToRoad();
    }

    private void SyncParkingLotTexturesToRoad()
    {
        Networks.ParkingLot.BaseColor = Networks.Road.BaseColor;
        Networks.ParkingLot.Normal = Networks.Road.Normal;
        Networks.ParkingLot.NormalStrength = Networks.Road.NormalStrength;
        Networks.ParkingLot.LowFrequencyScale = Networks.Road.LowFrequencyScale;
        Networks.ParkingLot.HighFrequencyScale = Networks.Road.HighFrequencyScale;
    }

    private void ApplySimpleOptionsProfile()
    {
        Terrain.Enabled = true;
        Networks.Enabled = true;
        Networks.RescanIntervalSeconds = 0f;
        Networks.Road.Enabled = true;
        Networks.RoadWear.Enabled = false;
        Networks.ParkingLot.Enabled = true;
        Networks.Gravel.Enabled = true;
    }
}

internal sealed class TerrainTextureConfig
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("lowFrequencyScale")]
    public float LowFrequencyScale { get; set; } = 80f;

    [JsonProperty("highFrequencyScale")]
    public float HighFrequencyScale { get; set; } = 200f;

    [JsonProperty("dirtHighFrequencyScale")]
    public float DirtHighFrequencyScale { get; set; } = 240f;

    [JsonProperty("grass")]
    public TerrainTextureSlot Grass { get; set; } = new("Textures/Grass_BaseColor.png", "Textures/Grass_Normal.png");

    [JsonProperty("dirt")]
    public TerrainTextureSlot Dirt { get; set; } = new("Textures/Dirt_BaseColor.png", "Textures/Dirt_Normal.png");

    [JsonProperty("rock")]
    public TerrainTextureSlot Rock { get; set; } = new("Textures/Cliff_BaseColor.png", "Textures/Cliff_Normal.png");

    public static TerrainTextureConfig CreateDefault() => new();

    public void Normalize()
    {
        Grass ??= new TerrainTextureSlot("Textures/Grass_BaseColor.png", "Textures/Grass_Normal.png");
        Dirt ??= new TerrainTextureSlot("Textures/Dirt_BaseColor.png", "Textures/Dirt_Normal.png");
        Rock ??= new TerrainTextureSlot("Textures/Cliff_BaseColor.png", "Textures/Cliff_Normal.png");
        Grass.Normalize();
        Dirt.Normalize();
        Rock.Normalize();
    }
}

internal sealed class TerrainTextureSlot
{
    public TerrainTextureSlot()
    {
    }

    public TerrainTextureSlot(string baseColor, string normal)
    {
        BaseColor = baseColor;
        Normal = normal;
    }

    [JsonProperty("baseColor")]
    public string BaseColor { get; set; } = string.Empty;

    [JsonProperty("normal")]
    public string Normal { get; set; } = string.Empty;

    [JsonProperty("normalStrength")]
    public float NormalStrength { get; set; } = 1f;

    public void Normalize()
    {
        if (NormalStrength < 0f)
        {
            NormalStrength = 0f;
        }
    }

}

internal sealed class NetworkTextureConfig
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("rescanIntervalSeconds")]
    public float RescanIntervalSeconds { get; set; } = 0f;

    [JsonProperty("road")]
    public TextureSlotConfig Road { get; set; } = TextureSlotConfig.Road();

    [JsonProperty("roadWear")]
    public TextureSlotConfig RoadWear { get; set; } = TextureSlotConfig.RoadWear();

    [JsonProperty("parkingLot")]
    public TextureSlotConfig ParkingLot { get; set; } = TextureSlotConfig.ParkingLot();

    [JsonProperty("sidewalk")]
    public TextureSlotConfig Sidewalk { get; set; } = TextureSlotConfig.Sidewalk();

    [JsonProperty("gravel")]
    public TextureSlotConfig Gravel { get; set; } = TextureSlotConfig.Gravel();

    public static NetworkTextureConfig CreateDefault() => new();

    public void Normalize()
    {
        if (RescanIntervalSeconds < 0f)
        {
            RescanIntervalSeconds = 0f;
        }

        Road ??= TextureSlotConfig.Road();
        RoadWear ??= TextureSlotConfig.RoadWear();
        ParkingLot ??= TextureSlotConfig.ParkingLot();
        Sidewalk ??= TextureSlotConfig.Sidewalk();
        Gravel ??= TextureSlotConfig.Gravel();
        Road.Normalize("road");
        RoadWear.Normalize("roadWear");
        ParkingLot.Normalize("parkingLot");
        Sidewalk.Normalize("sidewalk");
        Gravel.Normalize("gravel");

        Road.ApplyMatchingDefaults(TextureSlotConfig.Road(), replaceShaderKeywords: IsLegacyRoadShaderFilter(Road.ShaderNameKeywords));
        Road.MaterialNameKeywords = RemoveExactKeywords(Road.MaterialNameKeywords, "Parking", "ParkingLot", "Parking Lot", "CarPark", "Car Park");
        Road.TextureNameKeywords = RemoveExactKeywords(Road.TextureNameKeywords, "Parking", "ParkingLot", "Parking Lot", "CarPark", "Car Park");
        ParkingLot.ApplyMatchingDefaults(TextureSlotConfig.ParkingLot());
        Sidewalk.ApplyMatchingDefaults(TextureSlotConfig.Sidewalk());
        Sidewalk.MaterialNameKeywords = RemoveExactKeywords(Sidewalk.MaterialNameKeywords, "ParkingLane", "Parking Lane", "Parking_Lane", "ParkingRoad", "Parking Road", "RoadParking", "Road Parking");
        Sidewalk.TextureNameKeywords = RemoveExactKeywords(Sidewalk.TextureNameKeywords, "ParkingLane", "Parking Lane", "Parking_Lane", "ParkingRoad", "Parking Road", "RoadParking", "Road Parking");
        Sidewalk.ExcludeNameKeywords = Sidewalk.ExcludeNameKeywords
            .Where(value => !string.Equals(value, "Decal", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static bool IsLegacyRoadShaderFilter(string[] shaderNameKeywords)
    {
        return shaderNameKeywords.Length == 1 &&
            string.Equals(shaderNameKeywords[0], "NetCompositionMeshLitShader", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] RemoveExactKeywords(string[] keywords, params string[] removed)
    {
        return (keywords ?? Array.Empty<string>())
            .Where(value => !removed.Any(remove => string.Equals(value, remove, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}

internal sealed class TextureSlotConfig
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("baseColor")]
    public string BaseColor { get; set; } = string.Empty;

    [JsonProperty("normal")]
    public string Normal { get; set; } = string.Empty;

    [JsonProperty("normalStrength")]
    public float NormalStrength { get; set; } = 1f;

    [JsonProperty("baseColorShaderProperties")]
    public string[] BaseColorShaderProperties { get; set; } = Array.Empty<string>();

    [JsonProperty("normalShaderProperties")]
    public string[] NormalShaderProperties { get; set; } = Array.Empty<string>();

    [JsonProperty("lowFrequencyScale")]
    public float LowFrequencyScale { get; set; } = 1f;

    [JsonProperty("highFrequencyScale")]
    public float HighFrequencyScale { get; set; } = 1f;

    [JsonProperty("smoothness")]
    public float? Smoothness { get; set; }

    [JsonProperty("worldspaceUVScale")]
    public float? WorldspaceUVScale { get; set; }

    [JsonProperty("shaderNameKeywords")]
    public string[] ShaderNameKeywords { get; set; } = Array.Empty<string>();

    [JsonProperty("materialNameKeywords")]
    public string[] MaterialNameKeywords { get; set; } = Array.Empty<string>();

    [JsonProperty("textureNameKeywords")]
    public string[] TextureNameKeywords { get; set; } = Array.Empty<string>();

    [JsonProperty("excludeNameKeywords")]
    public string[] ExcludeNameKeywords { get; set; } = Array.Empty<string>();

    public static TextureSlotConfig Road()
    {
        return new TextureSlotConfig
        {
            Name = "road",
            BaseColor = "Textures/Road_BaseColor.png",
            Normal = "Textures/Road_Normal.png",
            BaseColorShaderProperties = new[] { "_WorldspaceAlbedo", "_BaseColorMap", "_BaseMap", "_MainTex" },
            NormalShaderProperties = new[] { "_WorldspaceNormalMap", "_NormalMap", "_BumpMap" },
            LowFrequencyScale = 1f,
            HighFrequencyScale = 1f,
            WorldspaceUVScale = 0.04f,
            Smoothness = null,
            ShaderNameKeywords = Array.Empty<string>(),
            MaterialNameKeywords = new[]
            {
                "Road_",
                "ParkingLane",
                "Parking Lane",
                "Parking_Lane",
                "ParkingRoad",
                "Parking Road",
                "RoadParking",
                "Road Parking",
                "Asphalt",
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
            },
            TextureNameKeywords = new[]
            {
                "RoadEUWorldspace_BaseColor",
                "RoadEUWorldspace",
                "RoadEU_BaseColor",
                "RoadEU",
                "Road_BaseColor",
                "Road_Normal",
                "ParkingLane",
                "Parking Lane",
                "Parking_Lane",
                "ParkingRoad",
                "Parking Road",
                "RoadParking",
                "Road Parking",
                "Asphalt",
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
            },
            ExcludeNameKeywords = new[] { "RoadWear", "Road Wear", "Wear", "Decal", "RoadParts", "Sidewalk", "Pavement", "Pedestrian", "Footpath", "Walkway", "Curb", "Kerb", "Marking", "LaneMarking", "Line", "Arrow" }
        };
    }

    public static TextureSlotConfig RoadWear()
    {
        return new TextureSlotConfig
        {
            Name = "roadWear",
            Enabled = false,
            BaseColor = "Textures/RoadWear_BaseColor.png",
            Normal = "Textures/RoadWear_Normal.png",
            LowFrequencyScale = 1f,
            HighFrequencyScale = 1f,
            Smoothness = null,
            ShaderNameKeywords = Array.Empty<string>(),
            MaterialNameKeywords = new[] { "RoadWear", "Road Wear" },
            TextureNameKeywords = new[] { "RoadWear", "Road Wear", "Wear" },
            ExcludeNameKeywords = Array.Empty<string>()
        };
    }

    public static TextureSlotConfig Sidewalk()
    {
        return new TextureSlotConfig
        {
            Name = "sidewalk",
            Enabled = false,
            BaseColor = "Textures/Sidewalk_BaseColor.png",
            Normal = "Textures/Sidewalk_Normal.png",
            BaseColorShaderProperties = new[] { "_BaseColorMap" },
            NormalShaderProperties = new[] { "_NormalMap" },
            LowFrequencyScale = 1f,
            HighFrequencyScale = 1f,
            Smoothness = null,
            ShaderNameKeywords = new[] { "NetCompositionMeshLitShader", "AreaDecalShader" },
            MaterialNameKeywords = new[]
            {
                "Road_",
                "TiledRoad",
                "PavementSurface",
                "Pavement_Surface",
                "Pavement Surface",
                "Pavement01",
                "Pavement_01",
                "Pavement 01",
                "Pavement Area"
            },
            TextureNameKeywords = new[]
            {
                "RoadEU_BaseColor",
                "RoadEU",
                "TiledRoad_BaseColor",
                "TiledRoad",
                "PavementSurface",
                "Pavement_Surface",
                "Pavement Surface",
                "Pavement01",
                "Pavement_01",
                "Pavement 01",
                "Pavement02",
                "Pavement_02",
                "Pavement 02"
            },
            ExcludeNameKeywords = new[] { "RoadWear", "Road Wear", "Wear", "RoadParts" }
        };
    }

    public static TextureSlotConfig ParkingLot()
    {
        return new TextureSlotConfig
        {
            Name = "parkingLot",
            BaseColor = "Textures/Road_BaseColor.png",
            Normal = "Textures/Road_Normal.png",
            BaseColorShaderProperties = new[] { "_BaseColorMap" },
            NormalShaderProperties = new[] { "_NormalMap" },
            LowFrequencyScale = 1f,
            HighFrequencyScale = 1f,
            Smoothness = null,
            ShaderNameKeywords = new[] { "NetCompositionMeshLitShader" },
            MaterialNameKeywords = new[]
            {
                "ParkingLane",
                "Parking Lane",
                "Parking_Lane",
                "ParkingRoad",
                "Parking Road",
                "RoadParking",
                "Road Parking"
            },
            TextureNameKeywords = new[]
            {
                "ParkingLane",
                "Parking Lane",
                "Parking_Lane",
                "ParkingRoad",
                "Parking Road",
                "RoadParking",
                "Road Parking"
            },
            ExcludeNameKeywords = new[] { "RoadWear", "Road Wear", "Wear", "RoadParts" }
        };
    }

    public static TextureSlotConfig Gravel()
    {
        return new TextureSlotConfig
        {
            Name = "gravel",
            BaseColor = "Textures/Gravel_BaseColor.png",
            Normal = "Textures/Gravel_Normal.png",
            LowFrequencyScale = 1f,
            HighFrequencyScale = 1f,
            Smoothness = null,
            ShaderNameKeywords = Array.Empty<string>(),
            MaterialNameKeywords = new[] { "GravelLane", "Gravel" },
            TextureNameKeywords = new[] { "GravelLane_BaseColor", "GravelLane", "Gravel" }
        };
    }

    public void Normalize(string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = fallbackName;
        }

        ShaderNameKeywords ??= Array.Empty<string>();
        MaterialNameKeywords ??= Array.Empty<string>();
        TextureNameKeywords ??= Array.Empty<string>();
        ExcludeNameKeywords ??= Array.Empty<string>();
        BaseColorShaderProperties ??= Array.Empty<string>();
        NormalShaderProperties ??= Array.Empty<string>();

        if (NormalStrength < 0f)
        {
            NormalStrength = 0f;
        }
    }

    public void ApplyMatchingDefaults(TextureSlotConfig defaults, bool replaceShaderKeywords = false)
    {
        if (replaceShaderKeywords)
        {
            ShaderNameKeywords = defaults.ShaderNameKeywords;
        }
        else
        {
            ShaderNameKeywords = MergeKeywords(ShaderNameKeywords, defaults.ShaderNameKeywords);
        }

        BaseColorShaderProperties = MergeKeywords(BaseColorShaderProperties, defaults.BaseColorShaderProperties);
        NormalShaderProperties = MergeKeywords(NormalShaderProperties, defaults.NormalShaderProperties);
        MaterialNameKeywords = MergeKeywords(MaterialNameKeywords, defaults.MaterialNameKeywords);
        TextureNameKeywords = MergeKeywords(TextureNameKeywords, defaults.TextureNameKeywords);
        ExcludeNameKeywords = MergeKeywords(ExcludeNameKeywords, defaults.ExcludeNameKeywords);
    }

    private static string[] MergeKeywords(string[] current, string[] defaults)
    {
        current ??= Array.Empty<string>();
        defaults ??= Array.Empty<string>();
        return current
            .Concat(defaults)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

internal sealed class FoliageConfig
{
    public const string PresetCustom = "custom";
    public const string PresetLow = "low";
    public const string PresetMedium = "medium";
    public const string PresetHigh = "high";
    public const int RoadMaskResolutionMin = 4096;
    public const int RoadMaskResolutionMax = 8192;
    public const float FoliageSpacingMin = 512f;
    public const float FoliageSpacingMax = 8192f;
    public const float FoliageSpacingDefault = 1536f;
    public const float FoliagePatchSizeMin = 64f;
    public const float FoliagePatchSizeMax = 512f;
    public const float FoliagePatchSizeDefault = 256f;
    public const float RoadMaskRefreshSecondsDefault = 300f;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonProperty("foliagePreset")]
    public string FoliagePreset { get; set; } = PresetCustom;

    [JsonProperty("forceVegetationSystem")]
    public bool ForceVegetationSystem { get; set; } = true;

    [JsonProperty("applyIntervalSeconds")]
    public float ApplyIntervalSeconds { get; set; } = 2f;

    [JsonProperty("applyEveryFrame")]
    public bool ApplyEveryFrame { get; set; } = true;

    [JsonProperty("forceVfxPlay")]
    public bool ForceVfxPlay { get; set; } = false;

    [JsonProperty("splatMapSource")]
    public string SplatMapSource { get; set; } = "roadMask";

    [JsonProperty("setTerrainSplatMap")]
    public bool SetTerrainSplatMap { get; set; } = true;

    [JsonProperty("setFoliageCoverage")]
    public bool SetFoliageCoverage { get; set; } = true;

    [JsonProperty("lockLightingToAwayFromSun")]
    public bool LockLightingToAwayFromSun { get; set; } = true;

    [JsonProperty("lightingDirectionOverride")]
    public float[]? LightingDirectionOverride { get; set; }

    [JsonProperty("debugSplatMapOverride")]
    public string DebugSplatMapOverride { get; set; } = "none";

    [JsonProperty("foliageCoverage")]
    public float? FoliageCoverage { get; set; } = FoliageSpacingDefault;

    [JsonProperty("foliageDensity")]
    public float? FoliageDensity { get; set; }

    [JsonProperty("foliageAutoParticleBudget")]
    public bool FoliageAutoParticleBudget { get; set; } = false;

    [JsonProperty("foliageParticleBudget")]
    public int FoliageParticleBudget { get; set; } = 850000;

    [JsonProperty("coverWholeMap")]
    public bool CoverWholeMap { get; set; } = false;

    [JsonProperty("foliagePatchSize")]
    public float FoliagePatchSize { get; set; } = FoliagePatchSizeDefault;

    [JsonProperty("foliageRenderDistance")]
    public float? FoliageRenderDistance { get; set; }

    [JsonProperty("foliageFadeStartDistance")]
    public float? FoliageFadeStartDistance { get; set; }

    [JsonProperty("foliageNearScale")]
    public float FoliageNearScale { get; set; } = 1f;

    [JsonProperty("foliageFarScale")]
    public float FoliageFarScale { get; set; } = 1f;

    [JsonProperty("texture")]
    public FoliageTextureConfig Texture { get; set; } = FoliageTextureConfig.CreateDefault();

    [JsonProperty("grassEnabled")]
    public bool? GrassEnabled { get; set; } = true;

    [JsonProperty("scatterEnabled")]
    public bool? ScatterEnabled { get; set; } = false;

    [JsonProperty("grassCoverage")]
    public float? GrassCoverage { get; set; }

    [JsonProperty("grassNormalScale")]
    public float? GrassNormalScale { get; set; }

    [JsonProperty("splatmapSamplingScale")]
    public float? SplatmapSamplingScale { get; set; }

    [JsonProperty("splatmapWeightBlendStrength")]
    public float? SplatmapWeightBlendStrength { get; set; }

    [JsonProperty("grassSplatIndexes")]
    public int[] GrassSplatIndexes { get; set; } = Array.Empty<int>();

    [JsonProperty("cropSize")]
    public float[]? CropSize { get; set; }

    [JsonProperty("terrainOffsetScale")]
    public float[]? TerrainOffsetScale { get; set; }

    [JsonProperty("roadMaskResolution")]
    public int RoadMaskResolution { get; set; } = RoadMaskResolutionMin;

    [JsonProperty("roadMaskRefreshSeconds")]
    public float RoadMaskRefreshSeconds { get; set; } = RoadMaskRefreshSecondsDefault;

    [JsonProperty("roadMaskWidthPadding")]
    public float RoadMaskWidthPadding { get; set; } = 1f;

    [JsonProperty("roadMaskTrackPadding")]
    public float RoadMaskTrackPadding { get; set; } = 1f;

    [JsonProperty("roadMaskNodePadding")]
    public float RoadMaskNodePadding { get; set; } = 1f;

    [JsonProperty("roadMaskBuildingPadding")]
    public float RoadMaskBuildingPadding { get; set; } = 1f;

    [JsonProperty("roadMaskSurfacePadding")]
    public float RoadMaskSurfacePadding { get; set; } = 0.5f;

    [JsonProperty("roadMaskIncludeRoads")]
    public bool RoadMaskIncludeRoads { get; set; } = true;

    [JsonProperty("roadMaskIncludeTracks")]
    public bool RoadMaskIncludeTracks { get; set; } = true;

    [JsonProperty("roadMaskIncludeBuildings")]
    public bool RoadMaskIncludeBuildings { get; set; } = true;

    [JsonProperty("roadMaskIncludeSurfaceAreas")]
    public bool RoadMaskIncludeSurfaceAreas { get; set; } = true;

    [JsonProperty("roadMaskNodeSamples")]
    public int RoadMaskNodeSamples { get; set; } = 48;

    [JsonProperty("roadMaskDrawNodeBounds")]
    public bool RoadMaskDrawNodeBounds { get; set; } = true;

    [JsonProperty("roadMaskCurveSamples")]
    public int RoadMaskCurveSamples { get; set; } = 16;

    [JsonProperty("roadMaskUseBaseSplatMap")]
    public bool RoadMaskUseBaseSplatMap { get; set; } = true;

    [JsonProperty("roadMaskFlipY")]
    public bool RoadMaskFlipY { get; set; } = true;

    [JsonProperty("logDiagnostics")]
    public bool LogDiagnostics { get; set; } = true;

    public static FoliageConfig CreateDefault() => new();

    public void Normalize()
    {
        ApplySimpleProfile();

        if (ApplyIntervalSeconds < 0f)
        {
            ApplyIntervalSeconds = 0f;
        }

        if (string.IsNullOrWhiteSpace(SplatMapSource))
        {
            SplatMapSource = "worldSplatmap";
        }

        GrassSplatIndexes ??= Array.Empty<int>();
        if (LightingDirectionOverride is { Length: < 3 })
        {
            LightingDirectionOverride = null;
        }

        if (string.IsNullOrWhiteSpace(DebugSplatMapOverride))
        {
            DebugSplatMapOverride = "none";
        }

        FoliagePatchSize = Math.Max(FoliagePatchSizeMin, Math.Min(FoliagePatchSizeMax, FoliagePatchSize));
        FoliageCoverage = Math.Max(
            FoliageSpacingMin,
            Math.Min(FoliageSpacingMax, FoliageCoverage ?? FoliageSpacingDefault));

        if (FoliageDensity.HasValue)
        {
            FoliageDensity = Math.Max(0f, FoliageDensity.Value);
        }

        FoliageParticleBudget = Math.Max(10000, Math.Min(1000000, FoliageParticleBudget));

        if (FoliageRenderDistance.HasValue)
        {
            FoliageRenderDistance = Math.Max(0f, FoliageRenderDistance.Value);
        }

        if (FoliageFadeStartDistance.HasValue)
        {
            FoliageFadeStartDistance = Math.Max(0f, FoliageFadeStartDistance.Value);
        }

        FoliageNearScale = Math.Max(0f, FoliageNearScale);
        FoliageFarScale = Math.Max(0f, FoliageFarScale);
        Texture ??= FoliageTextureConfig.CreateDefault();
        Texture.Normalize();

        if (GrassCoverage.HasValue)
        {
            GrassCoverage = Math.Max(0f, GrassCoverage.Value);
        }

        if (GrassNormalScale.HasValue)
        {
            GrassNormalScale = Math.Max(0f, GrassNormalScale.Value);
        }

        if (SplatmapSamplingScale.HasValue)
        {
            SplatmapSamplingScale = Math.Max(0f, SplatmapSamplingScale.Value);
        }

        if (SplatmapWeightBlendStrength.HasValue)
        {
            SplatmapWeightBlendStrength = Math.Max(0f, SplatmapWeightBlendStrength.Value);
        }

        RoadMaskResolution = Math.Max(RoadMaskResolutionMin, Math.Min(RoadMaskResolutionMax, RoadMaskResolution));
        RoadMaskRefreshSeconds = Math.Max(60f, RoadMaskRefreshSeconds);
        RoadMaskWidthPadding = Math.Max(0f, RoadMaskWidthPadding);
        RoadMaskTrackPadding = Math.Max(0f, RoadMaskTrackPadding);
        RoadMaskNodePadding = Math.Max(0f, RoadMaskNodePadding);
        RoadMaskBuildingPadding = Math.Max(0f, RoadMaskBuildingPadding);
        RoadMaskSurfacePadding = Math.Max(0f, RoadMaskSurfacePadding);
        RoadMaskCurveSamples = Math.Max(4, Math.Min(64, RoadMaskCurveSamples));
        RoadMaskNodeSamples = Math.Max(8, Math.Min(128, RoadMaskNodeSamples));
    }

    private void ApplySimpleProfile()
    {
        FoliagePreset = PresetCustom;
        ForceVegetationSystem = true;
        ApplyEveryFrame = true;
        ForceVfxPlay = false;
        SplatMapSource = "roadMask";
        SetTerrainSplatMap = true;
        SetFoliageCoverage = true;
        DebugSplatMapOverride = "none";
        FoliageDensity = null;
        FoliageAutoParticleBudget = false;
        CoverWholeMap = false;
        FoliageRenderDistance = null;
        FoliageFadeStartDistance = null;
        FoliageNearScale = 1f;
        FoliageFarScale = 1f;
        Texture ??= FoliageTextureConfig.CreateDefault();
        Texture.Enabled = false;
        Texture.MatchTerrainColor = false;
        GrassEnabled = true;
        ScatterEnabled = false;
        GrassCoverage = null;
        GrassNormalScale = null;
        SplatmapSamplingScale = null;
        SplatmapWeightBlendStrength = null;
        GrassSplatIndexes = Array.Empty<int>();
        FoliagePatchSize = Math.Max(FoliagePatchSizeMin, Math.Min(FoliagePatchSizeMax, FoliagePatchSize));
        CropSize = new[] { FoliagePatchSize, FoliagePatchSize };
        TerrainOffsetScale = null;
        RoadMaskRefreshSeconds = RoadMaskRefreshSecondsDefault;
        RoadMaskWidthPadding = 1f;
        RoadMaskTrackPadding = 1f;
        RoadMaskNodePadding = 1f;
        RoadMaskBuildingPadding = 1f;
        RoadMaskSurfacePadding = 0.5f;
        RoadMaskIncludeRoads = true;
        RoadMaskIncludeTracks = true;
        RoadMaskIncludeBuildings = true;
        RoadMaskIncludeSurfaceAreas = true;
        RoadMaskDrawNodeBounds = true;
        RoadMaskUseBaseSplatMap = true;
        RoadMaskFlipY = true;
        RoadMaskCurveSamples = 16;
        RoadMaskNodeSamples = 48;
    }
}

internal sealed class FoliageTextureConfig
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonProperty("baseColor")]
    public string BaseColor { get; set; } = "Textures/Grass_BaseColor.png";

    [JsonProperty("normal")]
    public string Normal { get; set; } = "Textures/Grass_Normal.png";

    [JsonProperty("mask")]
    public string Mask { get; set; } = string.Empty;

    [JsonProperty("normalStrength")]
    public float NormalStrength { get; set; } = 1f;

    [JsonProperty("matchTerrainColor")]
    public bool MatchTerrainColor { get; set; } = false;

    [JsonProperty("colorMatchStrength")]
    public float ColorMatchStrength { get; set; } = 1f;

    [JsonProperty("baseColorShaderProperties")]
    public string[] BaseColorShaderProperties { get; set; } =
    {
        "Grass_BaseColorMap",
        "Grass_BaseColor",
        "Grass_AlbedoMap",
        "Grass_Albedo",
        "Foliage_BaseColorMap",
        "Foliage_BaseColor",
        "Foliage_AlbedoMap",
        "Foliage_Albedo",
        "BaseColorMap",
        "Base Color Map",
        "_BaseColorMap",
        "_BaseMap",
        "_MainTex",
        "MainTex",
        "Albedo"
    };

    [JsonProperty("normalShaderProperties")]
    public string[] NormalShaderProperties { get; set; } =
    {
        "Grass_NormalMap",
        "Grass_Normal",
        "Foliage_NormalMap",
        "Foliage_Normal",
        "NormalMap",
        "Normal Map",
        "_NormalMap",
        "Normal"
    };

    [JsonProperty("maskShaderProperties")]
    public string[] MaskShaderProperties { get; set; } =
    {
        "Grass_MaskMap",
        "Grass_Mask",
        "Foliage_MaskMap",
        "Foliage_Mask",
        "MaskMap",
        "_MaskMap"
    };

    [JsonProperty("colorShaderProperties")]
    public string[] ColorShaderProperties { get; set; } =
    {
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

    [JsonProperty("gradientShaderProperties")]
    public string[] GradientShaderProperties { get; set; } =
    {
        "Grass_ColorGradient",
        "Grass_Gradient",
        "Foliage_ColorGradient",
        "Foliage_Gradient",
        "ColorGradient",
        "BaseColorGradient",
        "Gradient"
    };

    public static FoliageTextureConfig CreateDefault() => new();

    public void Normalize()
    {
        BaseColorShaderProperties ??= Array.Empty<string>();
        NormalShaderProperties ??= Array.Empty<string>();
        MaskShaderProperties ??= Array.Empty<string>();
        ColorShaderProperties ??= Array.Empty<string>();
        GradientShaderProperties ??= Array.Empty<string>();

        if (NormalStrength < 0f)
        {
            NormalStrength = 0f;
        }

        ColorMatchStrength = Math.Max(0f, Math.Min(1f, ColorMatchStrength));
    }
}
