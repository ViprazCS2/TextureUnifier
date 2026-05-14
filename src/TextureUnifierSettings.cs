using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.SceneFlow;
using Game.Settings;
using Game.UI.Localization;
using Game.UI.Widgets;

namespace TextureUnifier;

[FileLocation(TextureUnifierMod.DataFolderName)]
[SettingsUIShowGroupName]
public sealed class TextureUnifierSettings : ModSetting
{
    private const string GeneralTab = "General";
    private const string TerrainTab = "Terrain";
    private const string NetworksTab = "Networks";
    private const string FoliageTab = "Foliage";

    private const string MainGroup = "Main";
    private const string PacksGroup = "TexturePacks";
    private const string ActionsGroup = "Actions";
    private const string TimingGroup = "Timing";
    private const string TerrainScaleGroup = "TerrainScale";
    private const string NormalGroup = "Normals";
    private const string NetworkSlotsGroup = "NetworkSlots";
    private const string NetworkTuningGroup = "NetworkTuning";
    private const string FoliageVfxGroup = "FoliageVfx";
    private const string RoadMaskGroup = "RoadMask";

    private string _activeTexturePack = TexturePackManager.ManualPackValue;
    private string _selectedTextureSlot = "Road_BaseColor.png";
    private string _textureSourceFolder = string.Empty;
    private string _textureSourcePath = string.Empty;
    private string _lastTextureImport = "No texture imported this session.";
    private string[] _texturePackList = Array.Empty<string>();
    private int _texturePackListVersion;
    private int _textureImportVersion;

    public TextureUnifierSettings(IMod mod)
        : base(mod)
    {
        RefreshTexturePackList();
        EnsureTextureSourceFolderDefault();
        SetDefaults();
        LoadFromConfig();
    }

    [SettingsUISection(GeneralTab, MainGroup)]
    [SettingsUIDisplayName("", "Enable Texture Unifier")]
    [SettingsUIDescription("", "Master switch for terrain, roads, gravel, and grass texture changes.")]
    public bool Enabled { get; set; }

    [SettingsUIDropdown(typeof(TextureUnifierSettings), nameof(GenerateTexturePackItems))]
    [SettingsUIValueVersion(typeof(TextureUnifierSettings), nameof(GetTexturePackListVersion))]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Active texture pack")]
    [SettingsUIDescription("", "Choose a folder from ModsData/TextureUnifier/Packs and apply its standard texture file names immediately.")]
    public string ActiveTexturePack
    {
        get => _activeTexturePack;
        set
        {
            string next = value ?? TexturePackManager.ManualPackValue;
            if (string.Equals(_activeTexturePack, next, StringComparison.Ordinal))
            {
                return;
            }

            _activeTexturePack = next;
            SaveToConfig();
            TextureUnifierSystem.ActiveSystem?.RequestConfigReload();
        }
    }

    [SettingsUIDropdown(typeof(TextureUnifierSettings), nameof(GenerateTextureSlotItems))]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Texture slot")]
    [SettingsUIDescription("", "Choose which texture slot the next picked file should replace.")]
    public string SelectedTextureSlot
    {
        get => _selectedTextureSlot;
        set => _selectedTextureSlot = string.IsNullOrWhiteSpace(value) ? "Road_BaseColor.png" : value;
    }

    [SettingsUIDirectoryPicker]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Texture source folder")]
    [SettingsUIDescription("", "Folder scanned by the import button. Use Downloads, a texture library folder, or the default Texture Unifier inbox.")]
    public string TextureSourceFolder
    {
        get
        {
            EnsureTextureSourceFolderDefault();
            return _textureSourceFolder;
        }
        set => _textureSourceFolder = string.IsNullOrWhiteSpace(value) ? TextureUnifierPaths.ImportInboxPath : value.Trim();
    }

    [SettingsUITextInput]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Exact texture file path")]
    [SettingsUIDescription("", "Optional. Paste a PNG/JPG file path here to import that exact file; otherwise the source folder is checked for the selected slot filename.")]
    public string TextureSourcePath
    {
        get => _textureSourcePath;
        set => _textureSourcePath = value ?? string.Empty;
    }

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Import texture for slot")]
    [SettingsUIDescription("", "Imports the pasted file path, or a PNG/JPG in the source folder matching the selected slot filename.")]
    public bool PickTextureForSlot
    {
        set
        {
            if (value)
            {
                PickAndImportTextureForSlot();
            }
        }
    }

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Open texture source folder")]
    [SettingsUIDescription("", "Opens the folder scanned by Import texture for slot.")]
    public bool OpenTextureSourceFolder
    {
        set
        {
            if (value)
            {
                OpenFolder(TextureSourceFolder);
            }
        }
    }

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Open texture preview")]
    [SettingsUIDescription("", "Opens a visual preview sheet for the current texture pack or config paths.")]
    public bool OpenTexturePreview
    {
        set
        {
            if (value)
            {
                OpenTexturePreviewSheet();
            }
        }
    }

    [SettingsUIValueVersion(typeof(TextureUnifierSettings), nameof(GetTextureImportVersion))]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Last texture import")]
    [SettingsUIDescription("", "Shows the most recent import or preview action.")]
    public string LastTextureImport => _lastTextureImport;

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, MainGroup)]
    [SettingsUIDisplayName("", "Open texture folder")]
    [SettingsUIDescription("", "Opens the active texture folder where PNG/JPG replacement textures go.")]
    public bool OpenTexturePacksFolder
    {
        set
        {
            if (value)
            {
                OpenActiveTextureFolder();
            }
        }
    }

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, MainGroup)]
    [SettingsUIDisplayName("", "New texture folder")]
    [SettingsUIDescription("", "Creates and opens MyTexturePack, then makes it the active texture folder.")]
    public bool CreateTexturePackTemplate
    {
        set
        {
            if (value)
            {
                CreateNewTextureFolder();
            }
        }
    }

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, PacksGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Refresh texture packs")]
    [SettingsUIDescription("", "Refreshes the active texture pack dropdown after adding or removing pack folders.")]
    public bool RefreshTexturePacks
    {
        set
        {
            if (value)
            {
                RefreshTexturePackList();
            }
        }
    }

    [SettingsUISection(GeneralTab, TimingGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0.5f, max = 10f, step = 0.5f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Config auto-reload seconds")]
    [SettingsUIDescription("", "How often Texture Unifier checks config.json for external edits.")]
    public float AutoReloadSeconds { get; set; }

    [SettingsUISection(GeneralTab, TimingGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 1f, max = 30f, step = 1f)]
    [SettingsUIDisplayName("", "Terrain apply interval")]
    [SettingsUIDescription("", "How often terrain shader globals are refreshed.")]
    public float TerrainApplyIntervalSeconds { get; set; }

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, ActionsGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Open data folder")]
    [SettingsUIDescription("", "Opens the folder that contains config.json and the Textures directory.")]
    public bool OpenDataFolder
    {
        set
        {
            if (value)
            {
                OpenFolder(TextureUnifierPaths.RootPath);
            }
        }
    }

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, ActionsGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Reload from config.json")]
    [SettingsUIDescription("", "Refreshes this Options page from the current JSON config file.")]
    public bool ReloadFromConfig
    {
        set
        {
            if (value)
            {
                LoadFromConfig();
                TextureUnifierSystem.ActiveSystem?.RequestConfigReload();
            }
        }
    }

    [SettingsUIButton]
    [SettingsUISection(GeneralTab, ActionsGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Apply now")]
    [SettingsUIDescription("", "Writes these options to config.json and asks the live mod system to reload immediately.")]
    public bool ApplyNow
    {
        set
        {
            if (value)
            {
                SaveToConfig();
                TextureUnifierSystem.ActiveSystem?.RequestConfigReload();
            }
        }
    }

    [SettingsUIButton]
    [SettingsUIConfirmation("", "Restore Texture Unifier defaults and overwrite the common options in config.json?")]
    [SettingsUISection(GeneralTab, ActionsGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Restore defaults")]
    [SettingsUIDescription("", "Restores the release defaults for the options exposed in this page.")]
    public bool RestoreDefaults
    {
        set
        {
            if (value)
            {
                SetDefaults();
                SaveToConfig();
                TextureUnifierSystem.ActiveSystem?.RequestConfigReload();
            }
        }
    }

    [SettingsUISection(TerrainTab, MainGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Enable terrain textures")]
    [SettingsUIDescription("", "Replaces grass, dirt, and cliff terrain textures.")]
    public bool TerrainEnabled { get; set; }

    [SettingsUISection(TerrainTab, TerrainScaleGroup)]
    [SettingsUISlider(min = 1f, max = 500f, step = 1f)]
    [SettingsUIDisplayName("", "Close-up texture scale")]
    [SettingsUIDescription("", "Controls the fine terrain texture size seen near the camera.")]
    public float TerrainHighFrequencyScale { get; set; }

    [SettingsUISection(TerrainTab, TerrainScaleGroup)]
    [SettingsUISlider(min = 1f, max = 400f, step = 1f)]
    [SettingsUIDisplayName("", "Far-away texture scale")]
    [SettingsUIDescription("", "Controls the broad terrain texture size seen from farther away.")]
    public float TerrainLowFrequencyScale { get; set; }

    [SettingsUISection(TerrainTab, TerrainScaleGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 1f, max = 500f, step = 1f)]
    [SettingsUIDisplayName("", "Dirt high-frequency scale")]
    [SettingsUIDescription("", "Controls the dirt detail tiling channel.")]
    public float DirtHighFrequencyScale { get; set; }

    [SettingsUISection(TerrainTab, NormalGroup)]
    [SettingsUIAdvanced]
    [SettingsUISlider(min = 0f, max = 4f, step = 0.1f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Grass surface strength")]
    [SettingsUIDescription("", "Advanced. Changes how bumpy the grass detail map looks. Color textures use BaseColor files.")]
    public float GrassNormalStrength { get; set; }

    [SettingsUISection(TerrainTab, NormalGroup)]
    [SettingsUIAdvanced]
    [SettingsUISlider(min = 0f, max = 4f, step = 0.1f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Dirt surface strength")]
    [SettingsUIDescription("", "Advanced. Changes how bumpy the dirt detail map looks. Color textures use BaseColor files.")]
    public float DirtNormalStrength { get; set; }

    [SettingsUISection(TerrainTab, NormalGroup)]
    [SettingsUIAdvanced]
    [SettingsUISlider(min = 0f, max = 4f, step = 0.1f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Cliff surface strength")]
    [SettingsUIDescription("", "Advanced. Changes how bumpy the cliff detail map looks. Color textures use BaseColor files.")]
    public float RockNormalStrength { get; set; }

    [SettingsUISection(NetworksTab, MainGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Enable network textures")]
    [SettingsUIDescription("", "Replaces road, sidewalk, gravel, and optional road-wear material textures.")]
    public bool NetworksEnabled { get; set; }

    [SettingsUISection(NetworksTab, MainGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 1f, max = 60f, step = 1f)]
    [SettingsUIDisplayName("", "Network rescan interval")]
    [SettingsUIDescription("", "How often loaded materials are rescanned for road and sidewalk matches.")]
    public float NetworkRescanIntervalSeconds { get; set; }

    [SettingsUISection(NetworksTab, NetworkSlotsGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Road asphalt")]
    [SettingsUIDescription("", "Replaces the main drivable road texture.")]
    public bool RoadEnabled { get; set; }

    [SettingsUISection(NetworksTab, NetworkSlotsGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Road wear overlay")]
    [SettingsUIDescription("", "Optional replacement for CS2 road-wear overlay materials.")]
    public bool RoadWearEnabled { get; set; }

    [SettingsUISection(NetworksTab, NetworkSlotsGroup)]
    [SettingsUIAdvanced]
    [SettingsUIDisplayName("", "Change sidewalks")]
    [SettingsUIDescription("", "Advanced. Uses Sidewalk_BaseColor and Sidewalk_Normal from the active texture folder.")]
    public bool SidewalkEnabled { get; set; }

    [SettingsUISection(NetworksTab, NetworkSlotsGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Gravel")]
    [SettingsUIDescription("", "Replaces gravel lane/path materials when matching names are found.")]
    public bool GravelEnabled { get; set; }

    [SettingsUISection(NetworksTab, NetworkTuningGroup)]
    [SettingsUIAdvanced]
    [SettingsUISlider(min = 0f, max = 4f, step = 0.1f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Road surface strength")]
    [SettingsUIDescription("", "Advanced. Changes how bumpy the road detail map looks. Color textures use BaseColor files.")]
    public float RoadNormalStrength { get; set; }

    [SettingsUISection(NetworksTab, NetworkTuningGroup)]
    [SettingsUIAdvanced]
    [SettingsUISlider(min = 0f, max = 4f, step = 0.1f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Sidewalk surface strength")]
    [SettingsUIDescription("", "Advanced. Changes how bumpy the sidewalk detail map looks. Color textures use BaseColor files.")]
    public float SidewalkNormalStrength { get; set; }

    [SettingsUISection(NetworksTab, NetworkTuningGroup)]
    [SettingsUIAdvanced]
    [SettingsUISlider(min = 0f, max = 4f, step = 0.1f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Gravel surface strength")]
    [SettingsUIDescription("", "Advanced. Changes how bumpy the gravel detail map looks. Color textures use BaseColor files.")]
    public float GravelNormalStrength { get; set; }

    [SettingsUISection(NetworksTab, NetworkTuningGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 0.2f, step = 0.005f)]
    [SettingsUICustomFormat(fractionDigits = 3)]
    [SettingsUIDisplayName("", "Road world-space UV scale")]
    [SettingsUIDescription("", "Controls the world-space asphalt texture scale. Set to 0 to leave the material value unchanged.")]
    public float RoadWorldspaceUVScale { get; set; }

    [SettingsUISection(FoliageTab, MainGroup)]
    [SettingsUIDisplayName("", "Enable grass")]
    [SettingsUIDescription("", "Adds generated grass and keeps it out of roads, buildings, and paved areas.")]
    public bool FoliageEnabled { get; set; }

    [SettingsUISection(FoliageTab, MainGroup)]
    [SettingsUIAdvanced]
    [SettingsUISlider(min = 64f, max = 512f, step = 64f)]
    [SettingsUIDisplayName("", "Foliage Scale")]
    [SettingsUIDescription("", "Fixed VFX crop size around the camera. Higher values cover more area but can be expensive.")]
    public float FoliagePatchSize { get; set; }

    [SettingsUIDropdown(typeof(TextureUnifierSettings), nameof(GenerateFoliagePresetItems))]
    [SettingsUISection(FoliageTab, MainGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Foliage preset")]
    [SettingsUIDescription("", "Low, medium, and high apply safe grouped foliage values. Custom leaves the advanced values unchanged.")]
    public string FoliagePreset { get; set; } = FoliageConfig.PresetCustom;

    [SettingsUISection(FoliageTab, MainGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Apply foliage every frame")]
    [SettingsUIDescription("", "Keeps the foliage VFX override refreshed continuously.")]
    public bool FoliageApplyEveryFrame { get; set; }

    [SettingsUISection(FoliageTab, MainGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 20f, step = 0.5f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Foliage apply interval")]
    [SettingsUIDescription("", "How often foliage VFX settings are refreshed when per-frame refresh is off.")]
    public float FoliageApplyIntervalSeconds { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Replace foliage blade textures")]
    [SettingsUIDescription("", "Legacy option. The stock foliage VFX graph does not expose compatible blade texture inputs.")]
    public bool FoliageTextureEnabled { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Match foliage texture color")]
    [SettingsUIDescription("", "Legacy option. The stock foliage VFX graph does not expose compatible tint inputs.")]
    public bool FoliageMatchTerrainColor { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Use screen-space AO mask")]
    [SettingsUIDescription("", "Experimental. Uses HDRP ambient-occlusion buffers as the foliage mask base when available.")]
    public bool FoliageAmbientOcclusionMask { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Lock foliage lighting angle")]
    [SettingsUIDescription("", "Keeps stock foliage color/brightness stable by feeding the VFX graph the away-from-sun viewing direction.")]
    public bool FoliageLockLightingToAwayFromSun { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIAdvanced]
    [SettingsUISlider(min = 512f, max = 8192f, step = 128f)]
    [SettingsUIDisplayName("", "Foliage spacing")]
    [SettingsUIDescription("", "Stock VFX coverage/spacing value. Higher values make grass sparser.")]
    public float FoliageCoverage { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 500f, step = 1f)]
    [SettingsUIDisplayName("", "Foliage density")]
    [SettingsUIDescription("", "Density/spawn-rate value for exposed foliage VFX properties.")]
    public float FoliageDensity { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Auto particle budget")]
    [SettingsUIDescription("", "Automatically lowers effective foliage density when the stock VFX reaches its particle cap.")]
    public bool FoliageAutoParticleBudget { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 100000f, max = 1000000f, step = 50000f)]
    [SettingsUIDisplayName("", "Particle budget")]
    [SettingsUIDescription("", "Target live particle count before auto-budget spacing starts.")]
    public int FoliageParticleBudget { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Cover whole map")]
    [SettingsUIDescription("", "Uses the full terrain bounds for the foliage VFX crop area.")]
    public bool FoliageCoverWholeMap { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 64f, max = 4096f, step = 64f)]
    [SettingsUIDisplayName("", "Render distance")]
    [SettingsUIDescription("", "Distance where foliage scales/fades out.")]
    public float FoliageRenderDistance { get; set; }

    [SettingsUISection(FoliageTab, FoliageVfxGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 4096f, step = 64f)]
    [SettingsUIDisplayName("", "Fade start distance")]
    [SettingsUIDescription("", "Distance where foliage begins to fade. Set to 0 for automatic.")]
    public float FoliageFadeStartDistance { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 4096f, max = 8192f, step = 1024f)]
    [SettingsUIDisplayName("", "Road mask resolution")]
    [SettingsUIDescription("", "Texture resolution for the generated road carve-out mask. Presets never lower this value.")]
    public int RoadMaskResolution { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 1f, max = 30f, step = 1f)]
    [SettingsUIDisplayName("", "Road mask refresh seconds")]
    [SettingsUIDescription("", "How often the generated road carve-out mask is refreshed.")]
    public float RoadMaskRefreshSeconds { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 24f, step = 0.5f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Road edge padding")]
    [SettingsUIDescription("", "Extra road-edge width painted into the foliage mask.")]
    public float RoadMaskWidthPadding { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 32f, step = 0.5f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Road node padding")]
    [SettingsUIDescription("", "Extra intersection/node padding painted into the foliage mask.")]
    public float RoadMaskNodePadding { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Mask train/tram/subway tracks")]
    [SettingsUIDescription("", "Paints track networks into the foliage carve-out mask.")]
    public bool RoadMaskIncludeTracks { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Mask buildings")]
    [SettingsUIDescription("", "Paints building footprints into the foliage carve-out mask.")]
    public bool RoadMaskIncludeBuildings { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Mask surface areas")]
    [SettingsUIDescription("", "Paints placed surface areas into the foliage carve-out mask.")]
    public bool RoadMaskIncludeSurfaceAreas { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 24f, step = 0.5f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Track padding")]
    [SettingsUIDescription("", "Extra track width painted into the foliage mask.")]
    public float RoadMaskTrackPadding { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 32f, step = 0.5f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Building padding")]
    [SettingsUIDescription("", "Extra footprint padding painted around buildings.")]
    public float RoadMaskBuildingPadding { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUISlider(min = 0f, max = 32f, step = 0.5f)]
    [SettingsUICustomFormat(fractionDigits = 1)]
    [SettingsUIDisplayName("", "Surface padding")]
    [SettingsUIDescription("", "Extra footprint padding painted around surface areas.")]
    public float RoadMaskSurfacePadding { get; set; }

    [SettingsUISection(FoliageTab, RoadMaskGroup)]
    [SettingsUIHidden]
    [SettingsUIDisplayName("", "Log foliage diagnostics")]
    [SettingsUIDescription("", "Writes detailed foliage/VFX diagnostics to the mod log.")]
    public bool FoliageLogDiagnostics { get; set; }

    public DropdownItem<string>[] GenerateTexturePackItems()
    {
        var items = new List<DropdownItem<string>>
        {
            new()
            {
                value = TexturePackManager.ManualPackValue,
                displayName = LocalizedString.Value("Current config paths")
            }
        };

        foreach (string packName in _texturePackList)
        {
            items.Add(new DropdownItem<string>
            {
                value = packName,
                displayName = LocalizedString.Value(packName)
            });
        }

        if (!string.IsNullOrWhiteSpace(_activeTexturePack) &&
            !_texturePackList.Contains(_activeTexturePack, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new DropdownItem<string>
            {
                value = _activeTexturePack,
                displayName = LocalizedString.Value($"Missing: {_activeTexturePack}"),
                disabled = true
            });
        }

        return items.ToArray();
    }

    public DropdownItem<string>[] GenerateTextureSlotItems()
    {
        return TexturePackManager.Slots
            .Select(slot => new DropdownItem<string>
            {
                value = slot.FileName,
                displayName = LocalizedString.Value(slot.Optional ? $"{slot.Label} (optional)" : slot.Label)
            })
            .ToArray();
    }

    public DropdownItem<string>[] GenerateFoliagePresetItems()
    {
        return new[]
        {
            new DropdownItem<string>
            {
                value = FoliageConfig.PresetCustom,
                displayName = LocalizedString.Value("Custom")
            },
            new DropdownItem<string>
            {
                value = FoliageConfig.PresetLow,
                displayName = LocalizedString.Value("Low")
            },
            new DropdownItem<string>
            {
                value = FoliageConfig.PresetMedium,
                displayName = LocalizedString.Value("Medium")
            },
            new DropdownItem<string>
            {
                value = FoliageConfig.PresetHigh,
                displayName = LocalizedString.Value("High")
            }
        };
    }

    public int GetTexturePackListVersion()
    {
        return _texturePackListVersion;
    }

    public int GetTextureImportVersion()
    {
        return _textureImportVersion;
    }

    public override void SetDefaults()
    {
        CopyFromConfig(TextureUnifierConfig.CreateDefault());
    }

    public override void Apply()
    {
        SaveToConfig();
        TextureUnifierSystem.ActiveSystem?.RequestConfigReload();
        base.Apply();
    }

    internal void LoadFromConfig()
    {
        try
        {
            CopyFromConfig(TextureUnifierConfig.LoadOrCreate(TextureUnifierPaths.ConfigPath));
        }
        catch (Exception ex)
        {
            TextureUnifierMod.Log.Warn($"Texture Unifier options could not read config.json: {ex.Message}");
        }
    }

    private void SaveToConfig()
    {
        try
        {
            var config = TextureUnifierConfig.LoadOrCreate(TextureUnifierPaths.ConfigPath);
            CopyToConfig(config);
            config.Save(TextureUnifierPaths.ConfigPath);
        }
        catch (Exception ex)
        {
            TextureUnifierMod.Log.Warn($"Texture Unifier options could not write config.json: {ex.Message}");
        }
    }

    private void CopyFromConfig(TextureUnifierConfig config)
    {
        Enabled = config.Enabled;
        _activeTexturePack = config.ActiveTexturePack ?? TexturePackManager.ManualPackValue;
        AutoReloadSeconds = config.AutoReloadSeconds;
        TerrainApplyIntervalSeconds = config.TerrainApplyIntervalSeconds;

        TerrainEnabled = config.Terrain.Enabled;
        TerrainLowFrequencyScale = config.Terrain.LowFrequencyScale;
        TerrainHighFrequencyScale = config.Terrain.HighFrequencyScale;
        DirtHighFrequencyScale = config.Terrain.DirtHighFrequencyScale;
        GrassNormalStrength = config.Terrain.Grass.NormalStrength;
        DirtNormalStrength = config.Terrain.Dirt.NormalStrength;
        RockNormalStrength = config.Terrain.Rock.NormalStrength;

        NetworksEnabled = config.Networks.Enabled;
        NetworkRescanIntervalSeconds = config.Networks.RescanIntervalSeconds;
        RoadEnabled = config.Networks.Road.Enabled;
        RoadWearEnabled = config.Networks.RoadWear.Enabled;
        SidewalkEnabled = config.Networks.Sidewalk.Enabled;
        GravelEnabled = config.Networks.Gravel.Enabled;
        RoadNormalStrength = config.Networks.Road.NormalStrength;
        SidewalkNormalStrength = config.Networks.Sidewalk.NormalStrength;
        GravelNormalStrength = config.Networks.Gravel.NormalStrength;
        RoadWorldspaceUVScale = config.Networks.Road.WorldspaceUVScale ?? 0f;

        FoliageEnabled = config.Foliage.Enabled;
        FoliagePatchSize = config.Foliage.FoliagePatchSize;
        FoliagePreset = config.Foliage.FoliagePreset;
        FoliageApplyEveryFrame = config.Foliage.ApplyEveryFrame;
        FoliageApplyIntervalSeconds = config.Foliage.ApplyIntervalSeconds;
        FoliageTextureEnabled = config.Foliage.Texture.Enabled;
        FoliageMatchTerrainColor = config.Foliage.Texture.MatchTerrainColor;
        FoliageAmbientOcclusionMask = IsAmbientOcclusionSplatMapSource(config.Foliage.SplatMapSource);
        FoliageLockLightingToAwayFromSun = config.Foliage.LockLightingToAwayFromSun;
        FoliageCoverage = config.Foliage.FoliageCoverage ?? 0f;
        FoliageDensity = config.Foliage.FoliageDensity ?? 0f;
        FoliageAutoParticleBudget = config.Foliage.FoliageAutoParticleBudget;
        FoliageParticleBudget = config.Foliage.FoliageParticleBudget;
        FoliageCoverWholeMap = config.Foliage.CoverWholeMap;
        FoliageRenderDistance = config.Foliage.FoliageRenderDistance ?? 512f;
        FoliageFadeStartDistance = config.Foliage.FoliageFadeStartDistance ?? 0f;
        RoadMaskResolution = config.Foliage.RoadMaskResolution;
        RoadMaskRefreshSeconds = config.Foliage.RoadMaskRefreshSeconds;
        RoadMaskWidthPadding = config.Foliage.RoadMaskWidthPadding;
        RoadMaskNodePadding = config.Foliage.RoadMaskNodePadding;
        RoadMaskIncludeTracks = config.Foliage.RoadMaskIncludeTracks;
        RoadMaskIncludeBuildings = config.Foliage.RoadMaskIncludeBuildings;
        RoadMaskIncludeSurfaceAreas = config.Foliage.RoadMaskIncludeSurfaceAreas;
        RoadMaskTrackPadding = config.Foliage.RoadMaskTrackPadding;
        RoadMaskBuildingPadding = config.Foliage.RoadMaskBuildingPadding;
        RoadMaskSurfacePadding = config.Foliage.RoadMaskSurfacePadding;
        FoliageLogDiagnostics = config.Foliage.LogDiagnostics;
    }

    private void CopyToConfig(TextureUnifierConfig config)
    {
        config.Enabled = Enabled;
        config.ActiveTexturePack = _activeTexturePack ?? TexturePackManager.ManualPackValue;
        config.AutoReloadSeconds = AutoReloadSeconds;
        config.TerrainApplyIntervalSeconds = TerrainApplyIntervalSeconds;

        config.Terrain.Enabled = true;
        config.Terrain.LowFrequencyScale = TerrainLowFrequencyScale;
        config.Terrain.HighFrequencyScale = TerrainHighFrequencyScale;
        config.Terrain.DirtHighFrequencyScale = DirtHighFrequencyScale;
        config.Terrain.Grass.NormalStrength = GrassNormalStrength;
        config.Terrain.Dirt.NormalStrength = DirtNormalStrength;
        config.Terrain.Rock.NormalStrength = RockNormalStrength;

        config.Networks.Enabled = true;
        config.Networks.RescanIntervalSeconds = NetworkRescanIntervalSeconds;
        config.Networks.Road.Enabled = true;
        config.Networks.RoadWear.Enabled = false;
        config.Networks.ParkingLot.Enabled = true;
        config.Networks.Sidewalk.Enabled = SidewalkEnabled;
        config.Networks.Gravel.Enabled = true;
        config.Networks.Road.NormalStrength = RoadNormalStrength;
        config.Networks.ParkingLot.BaseColor = config.Networks.Road.BaseColor;
        config.Networks.ParkingLot.Normal = config.Networks.Road.Normal;
        config.Networks.ParkingLot.NormalStrength = RoadNormalStrength;
        config.Networks.Sidewalk.NormalStrength = SidewalkNormalStrength;
        config.Networks.Gravel.NormalStrength = GravelNormalStrength;
        config.Networks.Road.WorldspaceUVScale = RoadWorldspaceUVScale > 0f ? RoadWorldspaceUVScale : null;

        config.Foliage.Enabled = FoliageEnabled;
        config.Foliage.FoliagePatchSize = FoliagePatchSize;
        config.Foliage.FoliagePreset = FoliagePreset;
        config.Foliage.ApplyEveryFrame = FoliageApplyEveryFrame;
        config.Foliage.ApplyIntervalSeconds = FoliageApplyIntervalSeconds;
        config.Foliage.Texture.Enabled = FoliageTextureEnabled;
        config.Foliage.Texture.MatchTerrainColor = FoliageMatchTerrainColor;
        if (FoliageAmbientOcclusionMask)
        {
            config.Foliage.SplatMapSource = "roadMaskAmbientOcclusion";
        }
        else if (IsAmbientOcclusionSplatMapSource(config.Foliage.SplatMapSource))
        {
            config.Foliage.SplatMapSource = "roadMask";
        }

        config.Foliage.LockLightingToAwayFromSun = FoliageLockLightingToAwayFromSun;
        config.Foliage.FoliageCoverage = FoliageCoverage;
        config.Foliage.FoliageDensity = FoliageDensity;
        config.Foliage.FoliageAutoParticleBudget = FoliageAutoParticleBudget;
        config.Foliage.FoliageParticleBudget = FoliageParticleBudget;
        config.Foliage.CoverWholeMap = FoliageCoverWholeMap;
        config.Foliage.FoliageRenderDistance = FoliageRenderDistance > 0f ? FoliageRenderDistance : null;
        config.Foliage.FoliageFadeStartDistance = FoliageFadeStartDistance > 0f ? FoliageFadeStartDistance : null;
        config.Foliage.RoadMaskResolution = RoadMaskResolution;
        config.Foliage.RoadMaskRefreshSeconds = RoadMaskRefreshSeconds;
        config.Foliage.RoadMaskWidthPadding = RoadMaskWidthPadding;
        config.Foliage.RoadMaskNodePadding = RoadMaskNodePadding;
        config.Foliage.RoadMaskIncludeTracks = RoadMaskIncludeTracks;
        config.Foliage.RoadMaskIncludeBuildings = RoadMaskIncludeBuildings;
        config.Foliage.RoadMaskIncludeSurfaceAreas = RoadMaskIncludeSurfaceAreas;
        config.Foliage.RoadMaskTrackPadding = RoadMaskTrackPadding;
        config.Foliage.RoadMaskBuildingPadding = RoadMaskBuildingPadding;
        config.Foliage.RoadMaskSurfacePadding = RoadMaskSurfacePadding;
        config.Foliage.LogDiagnostics = FoliageLogDiagnostics;

        TexturePackManager.ApplyPack(config, config.ActiveTexturePack);
    }

    private void CreateNewTextureFolder()
    {
        TexturePackManager.CreateTemplatePack();
        _activeTexturePack = TexturePackManager.TemplatePackName;
        RefreshTexturePackList();
        SaveToConfig();
        TextureUnifierSystem.ActiveSystem?.RequestConfigReload();
        OpenFolder(TexturePackManager.GetPackFolderPath(_activeTexturePack));
    }

    private void OpenActiveTextureFolder()
    {
        string packName = TexturePackManager.GetWritablePackName(_activeTexturePack);
        bool changedActivePack = !string.Equals(_activeTexturePack, packName, StringComparison.Ordinal);
        _activeTexturePack = packName;
        RefreshTexturePackList();

        if (changedActivePack)
        {
            SaveToConfig();
            TextureUnifierSystem.ActiveSystem?.RequestConfigReload();
        }

        OpenFolder(TexturePackManager.GetPackFolderPath(packName));
    }

    private void PickAndImportTextureForSlot()
    {
        try
        {
            string packName = TexturePackManager.GetWritablePackName(_activeTexturePack);
            string? sourcePath = ResolveImportSourcePath();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                SetImportStatus("No PNG/JPG found. Put a texture in the source folder or paste an exact file path.");
                OpenFolder(TextureSourceFolder);
                return;
            }

            string importSourcePath = sourcePath!;
            string targetPath = TexturePackManager.ImportTexture(packName, _selectedTextureSlot, importSourcePath);
            _activeTexturePack = packName;
            RefreshTexturePackList();
            SaveToConfig();
            TextureUnifierSystem.ActiveSystem?.RequestConfigReload();
            SetImportStatus($"Imported {Path.GetFileName(importSourcePath)} as {Path.GetFileName(targetPath)} in {packName}. Use Open texture preview to inspect the pack.");
        }
        catch (Exception ex)
        {
            SetImportStatus($"Import failed: {ex.Message}");
            TextureUnifierMod.Log.Warn($"Texture Unifier import failed: {ex}");
        }
    }

    private string? ResolveImportSourcePath()
    {
        string requestedPath = (_textureSourcePath ?? string.Empty).Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            if (File.Exists(requestedPath))
            {
                if (!TexturePackManager.IsSupportedTextureExtension(Path.GetExtension(requestedPath)))
                {
                    throw new InvalidOperationException("Texture Unifier can import .png, .jpg, or .jpeg files.");
                }

                return requestedPath;
            }

            if (Directory.Exists(requestedPath))
            {
                _textureSourceFolder = requestedPath;
                _textureSourcePath = string.Empty;
            }
            else
            {
                throw new FileNotFoundException("Exact texture file path was not found.", requestedPath);
            }
        }

        EnsureTextureSourceFolderDefault();
        Directory.CreateDirectory(_textureSourceFolder);
        string? sourcePath = TexturePackManager.FindTextureFileForSlot(_textureSourceFolder, _selectedTextureSlot);
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            return sourcePath;
        }

        string selectedFileName = Path.GetFileName(_selectedTextureSlot);
        throw new FileNotFoundException(
            $"No texture matching {selectedFileName}, {Path.GetFileNameWithoutExtension(selectedFileName)}.jpg, or {Path.GetFileNameWithoutExtension(selectedFileName)}.jpeg was found in the source folder. To import a differently named file from a folder with multiple images, paste its exact path or move only that file into ImportInbox.",
            _textureSourceFolder);
    }

    private void OpenTexturePreviewSheet()
    {
        try
        {
            var config = TextureUnifierConfig.LoadOrCreate(TextureUnifierPaths.ConfigPath);
            CopyToConfig(config);
            config.Save(TextureUnifierPaths.ConfigPath);

            string previewPath = TexturePackManager.WritePreviewHtml(config, _selectedTextureSlot);
            SetImportStatus($"Opened texture preview for {(string.IsNullOrWhiteSpace(config.ActiveTexturePack) ? "current config paths" : config.ActiveTexturePack)}.");
            OpenFile(previewPath);
        }
        catch (Exception ex)
        {
            SetImportStatus($"Preview failed: {ex.Message}");
            TextureUnifierMod.Log.Warn($"Texture Unifier preview failed: {ex}");
        }
    }

    private void SetImportStatus(string message)
    {
        _lastTextureImport = message;
        _textureImportVersion++;
    }

    private void RefreshTexturePackList()
    {
        try
        {
            _texturePackList = TexturePackManager.GetPackNames();
            _texturePackListVersion++;
        }
        catch (Exception ex)
        {
            _texturePackList = Array.Empty<string>();
            _texturePackListVersion++;
            TextureUnifierMod.Log.Warn($"Texture Unifier could not refresh texture packs: {ex.Message}");
        }
    }

    private void EnsureTextureSourceFolderDefault()
    {
        if (string.IsNullOrWhiteSpace(_textureSourceFolder))
        {
            _textureSourceFolder = TextureUnifierPaths.ImportInboxPath;
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            OpenFile(path);
        }
        catch (Exception ex)
        {
            TextureUnifierMod.Log.Warn($"Texture Unifier could not open data folder: {ex.Message}");
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            TextureUnifierMod.Log.Warn($"Texture Unifier could not open file or folder: {ex.Message}");
        }
    }

    private static bool IsAmbientOcclusionSplatMapSource(string? source)
    {
        string normalized = (source ?? string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToLowerInvariant();

        return normalized is "ambientocclusion" or "ao" or "ssao" or "screenspaceambientocclusion" or
            "roadmaskambientocclusion" or "roadmaskao" or "aoroadmask";
    }
}

internal static class TextureUnifierLocalization
{
    public static void Load(TextureUnifierSettings settings)
    {
        try
        {
            var manager = GameManager.instance.localizationManager;
            string[] locales = manager.GetSupportedLocales();
            if (locales.Length == 0)
            {
                locales = new[] { "en-US" };
            }

            foreach (string locale in locales)
            {
                manager.AddSource(locale, new TextureUnifierLocaleSource(settings));
            }
        }
        catch (Exception ex)
        {
            TextureUnifierMod.Log.Warn($"Texture Unifier could not register option labels: {ex.Message}");
        }
    }
}

internal sealed class TextureUnifierLocaleSource : IDictionarySource
{
    private readonly Dictionary<string, string> _entries;

    public TextureUnifierLocaleSource(TextureUnifierSettings settings)
    {
        _entries = new Dictionary<string, string>
        {
            [settings.GetSettingsLocaleID()] = "Texture Unifier",
            [settings.GetOptionTabLocaleID("General")] = "General",
            [settings.GetOptionTabLocaleID("Terrain")] = "Terrain",
            [settings.GetOptionTabLocaleID("Networks")] = "Roads & More",
            [settings.GetOptionTabLocaleID("Foliage")] = "Grass",
            [settings.GetOptionGroupLocaleID("Main")] = "Main",
            [settings.GetOptionGroupLocaleID("TexturePacks")] = "Texture Folder",
            [settings.GetOptionGroupLocaleID("Actions")] = "Actions",
            [settings.GetOptionGroupLocaleID("Timing")] = "Timing",
            [settings.GetOptionGroupLocaleID("TerrainScale")] = "Scale",
            [settings.GetOptionGroupLocaleID("Normals")] = "Strength",
            [settings.GetOptionGroupLocaleID("NetworkSlots")] = "Slots",
            [settings.GetOptionGroupLocaleID("NetworkTuning")] = "Strength",
            [settings.GetOptionGroupLocaleID("FoliageVfx")] = "Grass",
            [settings.GetOptionGroupLocaleID("RoadMask")] = "Road Mask"
        };
    }

    public IEnumerable<KeyValuePair<string, string>> ReadEntries(
        IList<IDictionaryEntryError> errors,
        Dictionary<string, int> indexCounts)
    {
        return _entries;
    }

    public void Unload()
    {
    }
}
