using System;
using System.IO;
using Colossal.Logging;
using Game;

namespace TextureUnifier;

public sealed class TextureUnifierSystem : GameSystemBase
{
    private const float MinNetworkRescanIntervalSeconds = 30f;

    internal static TextureUnifierSystem? ActiveSystem { get; private set; }

    private ILog _log = TextureUnifierMod.Log;
    private string _rootPath = string.Empty;
    private string _texturesPath = string.Empty;
    private string _configPath = string.Empty;
    private DateTime _lastConfigWriteTime;
    private float _nextConfigCheck;
    private float _nextTerrainApply;
    private float _nextNetworkScan;
    private float _nextFoliageApply;
    private bool _networkDisabledAfterFailure;
    private bool _modWasEnabled;
    private bool _foliageWasApplied;
    private TextureUnifierConfig? _config;
    private TextureLoader? _textureLoader;
    private TerrainTextureApplicator? _terrainApplicator;
    private MaterialTextureApplicator? _materialApplicator;
    private FoliageVfxApplicator? _foliageApplicator;

    protected override void OnCreate()
    {
        base.OnCreate();

        ActiveSystem = this;
        _log = TextureUnifierMod.Log;
        _rootPath = TextureUnifierPaths.RootPath;
        _texturesPath = TextureUnifierPaths.TexturesPath;
        _configPath = TextureUnifierPaths.ConfigPath;

        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_texturesPath);
        Directory.CreateDirectory(TextureUnifierPaths.PacksPath);

        _textureLoader = new TextureLoader(_log);
        _terrainApplicator = new TerrainTextureApplicator(_rootPath, _textureLoader, _log);
        _materialApplicator = new MaterialTextureApplicator(_rootPath, _textureLoader, _log);
        _foliageApplicator = new FoliageVfxApplicator(_rootPath, _textureLoader, _log);

        ReloadConfig(force: true);
        _log.Info($"Texture Unifier data folder: {_rootPath}");
    }

    protected override void OnUpdate()
    {
        if (_textureLoader == null || _terrainApplicator == null || _materialApplicator == null || _foliageApplicator == null)
        {
            return;
        }

        float now = global::UnityEngine.Time.unscaledTime;

        if (_config == null || now >= _nextConfigCheck)
        {
            ReloadConfig(force: false);
        }

        if (_config == null)
        {
            return;
        }

        if (!_config.Enabled)
        {
            if (_modWasEnabled)
            {
                RestoreAll();
                _modWasEnabled = false;
                _foliageWasApplied = false;
            }

            _nextTerrainApply = now + _config.TerrainApplyIntervalSeconds;
            _nextNetworkScan = GetNextNetworkScanTime(now, _config.Networks);
            _nextFoliageApply = now + 1f;
            return;
        }

        _modWasEnabled = true;

        if (now >= _nextTerrainApply)
        {
            try
            {
                _terrainApplicator.Apply(_config.Terrain);
            }
            catch (Exception ex)
            {
                _log.Error($"Texture Unifier terrain pass failed: {ex}");
            }

            _nextTerrainApply = now + _config.TerrainApplyIntervalSeconds;
        }

        if (!_networkDisabledAfterFailure && now >= _nextNetworkScan)
        {
            try
            {
                _materialApplicator.Apply(_config.Networks);
            }
            catch (Exception ex)
            {
                _networkDisabledAfterFailure = true;
                _log.Error($"Texture Unifier network pass failed and was disabled until config reload: {ex}");
            }

            _nextNetworkScan = GetNextNetworkScanTime(now, _config.Networks);
        }

        if (!_config.Foliage.Enabled)
        {
            if (_foliageWasApplied)
            {
                _foliageApplicator.Restore(World);
                _foliageWasApplied = false;
            }

            _nextFoliageApply = now + 1f;
        }
        else if (_config.Foliage.ApplyEveryFrame)
        {
            _nextFoliageApply = now + 1f;
        }
        else if (now >= _nextFoliageApply)
        {
            try
            {
                _foliageApplicator.Apply(_config.Foliage, World);
                _foliageWasApplied = true;
            }
            catch (Exception ex)
            {
                _log.Error($"Texture Unifier foliage pass failed: {ex}");
            }

            _nextFoliageApply = GetNextFoliageApplyTime(now, _config.Foliage);
        }
    }

    protected override void OnDestroy()
    {
        RestoreAll();
        _textureLoader?.Dispose();
        _foliageApplicator?.Dispose();
        _textureLoader = null;
        _terrainApplicator = null;
        _materialApplicator = null;
        _foliageApplicator = null;

        if (ReferenceEquals(ActiveSystem, this))
        {
            ActiveSystem = null;
        }

        base.OnDestroy();
    }

    internal void RestoreAll()
    {
        _terrainApplicator?.Restore();
        _materialApplicator?.Restore();
        _foliageApplicator?.Restore(World);
        _foliageWasApplied = false;
    }

    internal void RequestConfigReload()
    {
        ReloadConfig(force: true);
    }

    internal void ApplyFoliageEveryFrame()
    {
        if (_config == null || !_config.Enabled || !_config.Foliage.Enabled || !_config.Foliage.ApplyEveryFrame)
        {
            return;
        }

        try
        {
            _foliageApplicator?.Apply(_config.Foliage, World);
            _foliageWasApplied = true;
        }
        catch (Exception ex)
        {
            _log.Error($"Texture Unifier per-frame foliage pass failed: {ex}");
        }
    }

    private void ReloadConfig(bool force)
    {
        float now = global::UnityEngine.Time.unscaledTime;
        float reloadDelay = _config?.AutoReloadSeconds ?? 2f;
        _nextConfigCheck = now + reloadDelay;

        DateTime writeTime = File.Exists(_configPath) ? File.GetLastWriteTimeUtc(_configPath) : DateTime.MinValue;
        if (!force && _config != null && writeTime == _lastConfigWriteTime)
        {
            return;
        }

        try
        {
            _config = TextureUnifierConfig.LoadOrCreate(_configPath);
            _lastConfigWriteTime = File.GetLastWriteTimeUtc(_configPath);
            _networkDisabledAfterFailure = false;
            _nextTerrainApply = now + 0.25f;
            _nextNetworkScan = now + 0.75f;
            _nextFoliageApply = _config.Foliage.Enabled && !_config.Foliage.ApplyEveryFrame
                ? now + 1.25f
                : now + 1f;
            _log.Info($"Texture Unifier config loaded: {_configPath}");
        }
        catch (Exception ex)
        {
            _log.Error($"Texture Unifier could not load config: {ex}");
        }
    }

    private static float GetNextFoliageApplyTime(float now, FoliageConfig config) =>
        config.Enabled && !config.ApplyEveryFrame && config.ApplyIntervalSeconds > 0f
            ? now + config.ApplyIntervalSeconds
            : now + 1f;

    private static float GetNextNetworkScanTime(float now, NetworkTextureConfig config) =>
        now + Math.Max(MinNetworkRescanIntervalSeconds, config.RescanIntervalSeconds);
}
