using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Logging;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using AreaGeometry = Game.Areas.Geometry;
using AreaNode = Game.Areas.Node;
using AreaSurface = Game.Areas.Surface;
using Building = Game.Buildings.Building;
using BuildingData = Game.Prefabs.BuildingData;
using ObjectGeometryData = Game.Prefabs.ObjectGeometryData;
using ObjectTransform = Game.Objects.Transform;
using PrefabRef = Game.Prefabs.PrefabRef;
using Temp = Game.Tools.Temp;
using Unspawned = Game.Objects.Unspawned;

namespace TextureUnifier;

internal sealed class FoliageRoadMaskGenerator : IDisposable
{
    private const float ZoneCellSizeMeters = 8f;
    private const float ChangeCheckIntervalSeconds = 0.25f;
    private const float ChangeRebuildDebounceSeconds = 0.35f;
    private const float SceneHashStepsPerMeter = 4f;

    private readonly ILog _log;
    private readonly Material? _material;
    private RenderTexture? _mask;
    private int _revision;
    private float _nextRefreshTime;
    private float _nextChangeCheckTime;
    private float _pendingRebuildTime;
    private string _lastSummary = string.Empty;
    private string _lastSignature = string.Empty;
    private string _pendingRebuildReason = string.Empty;
    private MaskSceneState _lastSceneState;
    private bool _hasSceneState;
    private bool _hasBuiltMask;

    public FoliageRoadMaskGenerator(ILog log)
    {
        _log = log;
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            _log.Warn("Texture Unifier road mask could not find Hidden/Internal-Colored shader.");
            return;
        }

        _material = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _material.SetInt("_SrcBlend", (int)BlendMode.One);
        _material.SetInt("_DstBlend", (int)BlendMode.Zero);
        _material.SetInt("_Cull", (int)CullMode.Off);
        _material.SetInt("_ZWrite", 0);
        _material.SetInt("_ZTest", (int)CompareFunction.Always);
    }

    public Texture? GetOrUpdate(FoliageConfig config, World world, Texture? baseSplatMap, FoliageTerrainBounds bounds, out string source, out int revision)
    {
        if (_material == null)
        {
            source = "roadMask:no material";
            revision = 0;
            return baseSplatMap;
        }

        int resolution = Mathf.Clamp(
            config.RoadMaskResolution,
            FoliageConfig.RoadMaskResolutionMin,
            FoliageConfig.RoadMaskResolutionMax);
        bool recreated = EnsureMask(resolution);

        float now = Time.unscaledTime;
        string signature = BuildStaticMaskSignature(config, baseSplatMap, resolution);
        bool signatureChanged = !string.Equals(signature, _lastSignature, StringComparison.Ordinal);
        string rebuildReason = recreated || !_hasBuiltMask
            ? "initial mask"
            : signatureChanged
                ? "mask settings changed"
                : string.Empty;

        if (rebuildReason.Length == 0)
        {
            string? sceneChange = DetectSceneChanges(config, world, now);
            if (sceneChange != null)
            {
                _pendingRebuildReason = sceneChange;
                _pendingRebuildTime = now + ChangeRebuildDebounceSeconds;
            }

            if (_pendingRebuildTime > 0f && now >= _pendingRebuildTime)
            {
                rebuildReason = _pendingRebuildReason.Length > 0 ? _pendingRebuildReason : "scene changed";
            }
            else if (now >= _nextRefreshTime)
            {
                rebuildReason = "safety resync";
            }
        }

        if (rebuildReason.Length > 0)
        {
            FoliageMaskCounts counts = Rebuild(config, world, baseSplatMap, bounds);
            _revision++;
            _hasBuiltMask = true;
            _lastSignature = signature;
            CaptureSceneBaseline(config, world, now);
            _pendingRebuildTime = 0f;
            _pendingRebuildReason = string.Empty;
            _nextRefreshTime = now + Mathf.Max(10f, config.RoadMaskRefreshSeconds);

            string summary = $"roadMask:{DescribeTexture(_mask)}, revision:{_revision}, reason:{rebuildReason}, roads:{counts.RoadEdgeCount}, tracks:{counts.TrackEdgeCount}, nodes:{counts.NodeCount}, buildings:{counts.BuildingCount}, surfaces:{counts.SurfaceAreaCount}, bounds:{bounds}";
            if (!string.Equals(summary, _lastSummary, StringComparison.Ordinal))
            {
                _lastSummary = summary;
                _log.Info($"Texture Unifier foliage {summary}");
            }
        }

        revision = _revision;
        source = $"roadMask={DescribeTexture(_mask)}, revision={_revision}, base={DescribeTexture(baseSplatMap)}";
        return _mask;
    }

    public void Dispose()
    {
        if (_mask != null)
        {
            _mask.Release();
            UnityEngine.Object.Destroy(_mask);
            _mask = null;
        }

        _lastSignature = string.Empty;
        _pendingRebuildReason = string.Empty;
        _hasSceneState = false;
        _pendingRebuildTime = 0f;
        _nextChangeCheckTime = 0f;
        _hasBuiltMask = false;
        _revision = 0;

        if (_material != null)
        {
            UnityEngine.Object.Destroy(_material);
        }
    }

    private bool EnsureMask(int resolution)
    {
        if (_mask != null && _mask.width == resolution && _mask.height == resolution)
        {
            return false;
        }

        if (_mask != null)
        {
            _mask.Release();
            UnityEngine.Object.Destroy(_mask);
        }

        _mask = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = $"TextureUnifier_FoliageRoadMask_{resolution}x{resolution}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false
        };
        _mask.Create();
        _hasBuiltMask = false;
        _lastSignature = string.Empty;
        _revision = 0;
        return true;
    }

    private static string BuildStaticMaskSignature(FoliageConfig config, Texture? baseSplatMap, int resolution)
    {
        string baseSignature = baseSplatMap == null
            ? "base:null"
            : FormattableString.Invariant($"base:{baseSplatMap.GetInstanceID()}:{baseSplatMap.width}x{baseSplatMap.height}:{baseSplatMap.dimension}");

        return string.Join("|", new[]
        {
            resolution.ToString(CultureInfo.InvariantCulture),
            baseSignature,
            Bool(config.RoadMaskUseBaseSplatMap),
            Bool(config.RoadMaskFlipY),
            Bool(config.RoadMaskIncludeRoads),
            Bool(config.RoadMaskIncludeTracks),
            Bool(config.RoadMaskIncludeBuildings),
            Bool(config.RoadMaskIncludeSurfaceAreas),
            Float(config.RoadMaskWidthPadding),
            Float(config.RoadMaskTrackPadding),
            Float(config.RoadMaskNodePadding),
            Float(config.RoadMaskBuildingPadding),
            Float(config.RoadMaskSurfacePadding),
            config.RoadMaskCurveSamples.ToString(CultureInfo.InvariantCulture),
            config.RoadMaskNodeSamples.ToString(CultureInfo.InvariantCulture),
            Bool(config.RoadMaskDrawNodeBounds)
        });
    }

    private string? DetectSceneChanges(FoliageConfig config, World world, float now)
    {
        if (now < _nextChangeCheckTime)
        {
            return null;
        }

        _nextChangeCheckTime = now + ChangeCheckIntervalSeconds;
        EntityManager entityManager = world.EntityManager;

        MaskSceneState state = BuildSceneState(config, entityManager);
        if (!_hasSceneState)
        {
            _lastSceneState = state;
            _hasSceneState = true;
            return null;
        }

        bool changed = !state.Equals(_lastSceneState);
        _lastSceneState = state;
        return changed ? $"scene changed ({state})" : null;
    }

    private void CaptureSceneBaseline(FoliageConfig config, World world, float now)
    {
        EntityManager entityManager = world.EntityManager;
        _lastSceneState = BuildSceneState(config, entityManager);
        _hasSceneState = true;
        _nextChangeCheckTime = now + ChangeCheckIntervalSeconds;
    }

    private static MaskSceneState BuildSceneState(FoliageConfig config, EntityManager entityManager)
    {
        QueryChangeState networks = config.RoadMaskIncludeRoads || config.RoadMaskIncludeTracks
            ? GetNetworkChangeState(config, entityManager)
            : default;
        QueryChangeState buildings = config.RoadMaskIncludeBuildings
            ? GetBuildingChangeState(entityManager)
            : default;
        QueryChangeState surfaces = config.RoadMaskIncludeSurfaceAreas
            ? GetSurfaceChangeState(entityManager)
            : default;

        return new MaskSceneState(networks, buildings, surfaces);
    }

    private static QueryChangeState GetNetworkChangeState(FoliageConfig config, EntityManager entityManager)
    {
        EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<EdgeGeometry>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Temp>()
            }
        });

        try
        {
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            NativeArray<EdgeGeometry> geometries = query.ToComponentDataArray<EdgeGeometry>(Allocator.Temp);
            try
            {
                int count = 0;
                int hash = HashStart();
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    bool isRoad = config.RoadMaskIncludeRoads && entityManager.HasComponent<Road>(entity);
                    bool isTrack = config.RoadMaskIncludeTracks &&
                        (entityManager.HasComponent<TrainTrack>(entity) ||
                         entityManager.HasComponent<TramTrack>(entity) ||
                         entityManager.HasComponent<SubwayTrack>(entity));
                    if (!isRoad && !isTrack)
                    {
                        continue;
                    }

                    count++;
                    int entryHash = HashStart();
                    entryHash = Mix(entryHash, entity.Index);
                    entryHash = Mix(entryHash, entity.Version);
                    entryHash = Mix(entryHash, isRoad ? 1 : 2);
                    entryHash = HashSegment(entryHash, geometries[i].m_Start);
                    entryHash = HashSegment(entryHash, geometries[i].m_End);

                    if (entityManager.HasComponent<StartNodeGeometry>(entity))
                    {
                        entryHash = HashBounds(entryHash, entityManager.GetComponentData<StartNodeGeometry>(entity).m_Geometry.m_Bounds);
                    }

                    if (entityManager.HasComponent<EndNodeGeometry>(entity))
                    {
                        entryHash = HashBounds(entryHash, entityManager.GetComponentData<EndNodeGeometry>(entity).m_Geometry.m_Bounds);
                    }

                    hash = AddUnorderedHash(hash, entryHash);
                }

                return new QueryChangeState(count, hash);
            }
            finally
            {
                if (entities.IsCreated)
                {
                    entities.Dispose();
                }

                if (geometries.IsCreated)
                {
                    geometries.Dispose();
                }
            }
        }
        finally
        {
            query.Dispose();
        }
    }

    private static QueryChangeState GetBuildingChangeState(EntityManager entityManager)
    {
        EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ObjectTransform>(),
                ComponentType.ReadOnly<PrefabRef>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Unspawned>()
            }
        });

        try
        {
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            NativeArray<ObjectTransform> transforms = query.ToComponentDataArray<ObjectTransform>(Allocator.Temp);
            NativeArray<PrefabRef> prefabRefs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            try
            {
                int hash = HashStart();
                for (int i = 0; i < entities.Length; i++)
                {
                    int entryHash = HashStart();
                    entryHash = Mix(entryHash, entities[i].Index);
                    entryHash = Mix(entryHash, entities[i].Version);
                    entryHash = HashFloat3(entryHash, transforms[i].m_Position);
                    entryHash = HashQuaternion(entryHash, transforms[i].m_Rotation);
                    entryHash = Mix(entryHash, prefabRefs[i].m_Prefab.Index);
                    entryHash = Mix(entryHash, prefabRefs[i].m_Prefab.Version);
                    hash = AddUnorderedHash(hash, entryHash);
                }

                return new QueryChangeState(entities.Length, hash);
            }
            finally
            {
                if (entities.IsCreated)
                {
                    entities.Dispose();
                }

                if (transforms.IsCreated)
                {
                    transforms.Dispose();
                }

                if (prefabRefs.IsCreated)
                {
                    prefabRefs.Dispose();
                }
            }
        }
        finally
        {
            query.Dispose();
        }
    }

    private static QueryChangeState GetSurfaceChangeState(EntityManager entityManager)
    {
        EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<AreaSurface>(),
                ComponentType.ReadOnly<AreaGeometry>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Temp>()
            }
        });

        try
        {
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            NativeArray<AreaGeometry> geometries = query.ToComponentDataArray<AreaGeometry>(Allocator.Temp);
            try
            {
                int hash = HashStart();
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    int entryHash = HashStart();
                    entryHash = Mix(entryHash, entity.Index);
                    entryHash = Mix(entryHash, entity.Version);
                    entryHash = HashBounds(entryHash, geometries[i].m_Bounds);

                    if (!entityManager.HasBuffer<AreaNode>(entity))
                    {
                        hash = AddUnorderedHash(hash, entryHash);
                        continue;
                    }

                    DynamicBuffer<AreaNode> nodes = entityManager.GetBuffer<AreaNode>(entity, isReadOnly: true);
                    entryHash = Mix(entryHash, nodes.Length);
                    for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
                    {
                        entryHash = HashFloat3(entryHash, nodes[nodeIndex].m_Position);
                    }

                    hash = AddUnorderedHash(hash, entryHash);
                }

                return new QueryChangeState(entities.Length, hash);
            }
            finally
            {
                if (entities.IsCreated)
                {
                    entities.Dispose();
                }

                if (geometries.IsCreated)
                {
                    geometries.Dispose();
                }
            }
        }
        finally
        {
            query.Dispose();
        }
    }

    private static int HashStart()
    {
        unchecked
        {
            return (int)2166136261u;
        }
    }

    private static int Mix(int hash, int value)
    {
        unchecked
        {
            return (hash * 16777619) ^ value;
        }
    }

    private static int AddUnorderedHash(int aggregateHash, int entryHash)
    {
        unchecked
        {
            return aggregateHash + (entryHash * 16777619) + RotateLeft(entryHash, 13);
        }
    }

    private static int RotateLeft(int value, int offset)
    {
        uint unsigned = unchecked((uint)value);
        return unchecked((int)((unsigned << offset) | (unsigned >> (32 - offset))));
    }

    private static int HashFloat(int hash, float value)
    {
        return Mix(hash, Mathf.RoundToInt(value * SceneHashStepsPerMeter));
    }

    private static int HashFloat3(int hash, float3 value)
    {
        hash = HashFloat(hash, value.x);
        hash = HashFloat(hash, value.y);
        return HashFloat(hash, value.z);
    }

    private static int HashQuaternion(int hash, quaternion value)
    {
        hash = HashFloat(hash, value.value.x);
        hash = HashFloat(hash, value.value.y);
        hash = HashFloat(hash, value.value.z);
        return HashFloat(hash, value.value.w);
    }

    private static int HashBounds(int hash, Bounds3 bounds)
    {
        hash = HashFloat3(hash, bounds.min);
        return HashFloat3(hash, bounds.max);
    }

    private static int HashBezier(int hash, Bezier4x3 bezier)
    {
        hash = HashFloat3(hash, bezier.a);
        hash = HashFloat3(hash, bezier.b);
        hash = HashFloat3(hash, bezier.c);
        return HashFloat3(hash, bezier.d);
    }

    private static int HashSegment(int hash, Segment segment)
    {
        hash = HashBezier(hash, segment.m_Left);
        return HashBezier(hash, segment.m_Right);
    }

    private static bool QueryDidChangeNetwork(EntityManager entityManager, EntityQuery query, uint since)
    {
        var edgeHandle = entityManager.GetComponentTypeHandle<Edge>(isReadOnly: true);
        var geometryHandle = entityManager.GetComponentTypeHandle<EdgeGeometry>(isReadOnly: true);
        NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
        try
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                ArchetypeChunk chunk = chunks[i];
                if (chunk.DidOrderChange(since) ||
                    chunk.DidChange(ref edgeHandle, since) ||
                    chunk.DidChange(ref geometryHandle, since))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (chunks.IsCreated)
            {
                chunks.Dispose();
            }
        }
    }

    private static bool QueryDidChangeBuildings(EntityManager entityManager, EntityQuery query, uint since)
    {
        var buildingHandle = entityManager.GetComponentTypeHandle<Building>(isReadOnly: true);
        var transformHandle = entityManager.GetComponentTypeHandle<ObjectTransform>(isReadOnly: true);
        var prefabHandle = entityManager.GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
        NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
        try
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                ArchetypeChunk chunk = chunks[i];
                if (chunk.DidOrderChange(since) ||
                    chunk.DidChange(ref buildingHandle, since) ||
                    chunk.DidChange(ref transformHandle, since) ||
                    chunk.DidChange(ref prefabHandle, since))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (chunks.IsCreated)
            {
                chunks.Dispose();
            }
        }
    }

    private static bool QueryDidChangeSurfaces(EntityManager entityManager, EntityQuery query, uint since)
    {
        var surfaceHandle = entityManager.GetComponentTypeHandle<AreaSurface>(isReadOnly: true);
        var geometryHandle = entityManager.GetComponentTypeHandle<AreaGeometry>(isReadOnly: true);
        NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
        try
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                ArchetypeChunk chunk = chunks[i];
                if (chunk.DidOrderChange(since) ||
                    chunk.DidChange(ref surfaceHandle, since) ||
                    chunk.DidChange(ref geometryHandle, since))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (chunks.IsCreated)
            {
                chunks.Dispose();
            }
        }
    }

    private static string Bool(bool value)
    {
        return value ? "1" : "0";
    }

    private static string Float(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private FoliageMaskCounts Rebuild(FoliageConfig config, World world, Texture? baseSplatMap, FoliageTerrainBounds bounds)
    {
        if (_mask == null)
        {
            return default;
        }

        RenderTexture? previous = RenderTexture.active;
        RenderTexture.active = _mask;

        if (baseSplatMap != null && config.RoadMaskUseBaseSplatMap)
        {
            Graphics.Blit(baseSplatMap, _mask);
            RenderTexture.active = _mask;
        }
        else
        {
            GL.Clear(clearDepth: false, clearColor: true, Color.white);
        }

        FoliageMaskCounts counts = DrawNetworkMask(config, world, bounds, _mask.width, _mask.height);
        counts.Add(DrawBuildingMask(config, world, bounds, _mask.width, _mask.height));
        counts.Add(DrawSurfaceAreaMask(config, world, bounds, _mask.width, _mask.height));

        RenderTexture.active = previous;
        return counts;
    }

    private FoliageMaskCounts DrawNetworkMask(FoliageConfig config, World world, FoliageTerrainBounds bounds, int width, int height)
    {
        if (_material == null || (!config.RoadMaskIncludeRoads && !config.RoadMaskIncludeTracks))
        {
            return default;
        }

        EntityManager entityManager = world.EntityManager;
        EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<EdgeGeometry>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Temp>()
            }
        });

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        NativeArray<Edge> edges = query.ToComponentDataArray<Edge>(Allocator.Temp);
        NativeArray<EdgeGeometry> geometries = query.ToComponentDataArray<EdgeGeometry>(Allocator.Temp);
        query.Dispose();

        try
        {
            var drawnNodeBounds = new HashSet<Entity>();

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, height, 0f);
            _material.SetPass(0);

            var counts = new FoliageMaskCounts();
            for (int i = 0; i < geometries.Length; i++)
            {
                Entity entity = entities[i];
                bool isRoad = config.RoadMaskIncludeRoads && entityManager.HasComponent<Road>(entity);
                bool isTrack = config.RoadMaskIncludeTracks &&
                    (entityManager.HasComponent<TrainTrack>(entity) ||
                     entityManager.HasComponent<TramTrack>(entity) ||
                     entityManager.HasComponent<SubwayTrack>(entity));
                if (!isRoad && !isTrack)
                {
                    continue;
                }

                float edgePadding = isTrack && !isRoad ? config.RoadMaskTrackPadding : config.RoadMaskWidthPadding;
                Edge edge = edges[i];
                EdgeGeometry geometry = geometries[i];

                DrawSegment(geometry.m_Start, edgePadding, config, bounds, width, height);
                DrawSegment(geometry.m_End, edgePadding, config, bounds, width, height);

                if (isRoad)
                {
                    counts.RoadEdgeCount++;
                }
                else
                {
                    counts.TrackEdgeCount++;
                }

                if (entityManager.HasComponent<StartNodeGeometry>(entity))
                {
                    DrawEdgeNodeGeometry(entityManager.GetComponentData<StartNodeGeometry>(entity).m_Geometry, edgePadding, config, bounds, width, height);
                    counts.NodeCount++;
                }

                if (entityManager.HasComponent<EndNodeGeometry>(entity))
                {
                    DrawEdgeNodeGeometry(entityManager.GetComponentData<EndNodeGeometry>(entity).m_Geometry, edgePadding, config, bounds, width, height);
                    counts.NodeCount++;
                }

                if (config.RoadMaskDrawNodeBounds)
                {
                    counts.NodeCount += DrawNodeBounds(edge.m_Start, entityManager, drawnNodeBounds, edgePadding, config, bounds, width, height);
                    counts.NodeCount += DrawNodeBounds(edge.m_End, entityManager, drawnNodeBounds, edgePadding, config, bounds, width, height);
                }
            }

            GL.PopMatrix();
            return counts;
        }
        finally
        {
            if (entities.IsCreated)
            {
                entities.Dispose();
            }

            if (edges.IsCreated)
            {
                edges.Dispose();
            }

            if (geometries.IsCreated)
            {
                geometries.Dispose();
            }
        }
    }

    private FoliageMaskCounts DrawBuildingMask(FoliageConfig config, World world, FoliageTerrainBounds bounds, int width, int height)
    {
        if (_material == null || !config.RoadMaskIncludeBuildings)
        {
            return default;
        }

        EntityManager entityManager = world.EntityManager;
        EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ObjectTransform>(),
                ComponentType.ReadOnly<PrefabRef>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Unspawned>()
            }
        });

        NativeArray<ObjectTransform> transforms = query.ToComponentDataArray<ObjectTransform>(Allocator.Temp);
        NativeArray<PrefabRef> prefabRefs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp);
        query.Dispose();

        try
        {
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, height, 0f);
            _material.SetPass(0);

            var counts = new FoliageMaskCounts();
            for (int i = 0; i < transforms.Length; i++)
            {
                if (!TryGetBuildingLocalBounds(entityManager, prefabRefs[i].m_Prefab, out Vector2 min, out Vector2 max))
                {
                    continue;
                }

                DrawRotatedQuad(transforms[i].m_Position, transforms[i].m_Rotation, min, max, config.RoadMaskBuildingPadding, config, bounds, width, height);
                counts.BuildingCount++;
            }

            GL.PopMatrix();
            return counts;
        }
        finally
        {
            if (transforms.IsCreated)
            {
                transforms.Dispose();
            }

            if (prefabRefs.IsCreated)
            {
                prefabRefs.Dispose();
            }
        }
    }

    private FoliageMaskCounts DrawSurfaceAreaMask(FoliageConfig config, World world, FoliageTerrainBounds bounds, int width, int height)
    {
        if (_material == null || !config.RoadMaskIncludeSurfaceAreas)
        {
            return default;
        }

        EntityManager entityManager = world.EntityManager;
        EntityQuery query = entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<AreaSurface>(),
                ComponentType.ReadOnly<AreaGeometry>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Temp>()
            }
        });

        NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        NativeArray<AreaGeometry> geometries = query.ToComponentDataArray<AreaGeometry>(Allocator.Temp);
        query.Dispose();

        try
        {
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, height, 0f);
            _material.SetPass(0);

            var counts = new FoliageMaskCounts();
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (entityManager.HasBuffer<AreaNode>(entity))
                {
                    DynamicBuffer<AreaNode> nodes = entityManager.GetBuffer<AreaNode>(entity, isReadOnly: true);
                    if (DrawAreaPolygon(nodes, config.RoadMaskSurfacePadding, config, bounds, width, height))
                    {
                        counts.SurfaceAreaCount++;
                        continue;
                    }
                }

                DrawRoundedBounds(geometries[i].m_Bounds, config.RoadMaskSurfacePadding, config, bounds, width, height);
                counts.SurfaceAreaCount++;
            }

            GL.PopMatrix();
            return counts;
        }
        finally
        {
            if (entities.IsCreated)
            {
                entities.Dispose();
            }

            if (geometries.IsCreated)
            {
                geometries.Dispose();
            }
        }
    }

    private static void DrawEdgeNodeGeometry(EdgeNodeGeometry geometry, float edgePadding, FoliageConfig config, FoliageTerrainBounds bounds, int width, int height)
    {
        DrawSegment(geometry.m_Left, edgePadding, config, bounds, width, height);
        DrawSegment(geometry.m_Right, edgePadding, config, bounds, width, height);
        DrawRoundedBounds(geometry.m_Bounds, edgePadding + config.RoadMaskNodePadding, config, bounds, width, height);
    }

    private static int DrawNodeBounds(
        Entity node,
        EntityManager entityManager,
        HashSet<Entity> drawnNodeBounds,
        float edgePadding,
        FoliageConfig config,
        FoliageTerrainBounds bounds,
        int width,
        int height)
    {
        if (node == Entity.Null || !drawnNodeBounds.Add(node) || !entityManager.HasComponent<NodeGeometry>(node))
        {
            return 0;
        }

        DrawRoundedBounds(entityManager.GetComponentData<NodeGeometry>(node).m_Bounds, edgePadding + config.RoadMaskNodePadding, config, bounds, width, height);
        return 1;
    }

    private static void DrawSegment(Segment segment, float padding, FoliageConfig config, FoliageTerrainBounds bounds, int width, int height)
    {
        GL.Begin(GL.TRIANGLE_STRIP);
        GL.Color(Color.black);

        int samples = config.RoadMaskCurveSamples;
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector2 left = ToWorldXZ(Evaluate(segment.m_Left, t));
            Vector2 right = ToWorldXZ(Evaluate(segment.m_Right, t));
            ExpandPair(ref left, ref right, padding);

            Vector2 leftPixel = bounds.WorldToPixel(left, width, height, config.RoadMaskFlipY);
            Vector2 rightPixel = bounds.WorldToPixel(right, width, height, config.RoadMaskFlipY);

            GL.Vertex3(leftPixel.x, leftPixel.y, 0f);
            GL.Vertex3(rightPixel.x, rightPixel.y, 0f);
        }

        GL.End();
    }

    private static void DrawRoundedBounds(Bounds3 bounds3, float padding, FoliageConfig config, FoliageTerrainBounds terrainBounds, int width, int height)
    {
        Vector2 min = new(bounds3.min.x, bounds3.min.z);
        Vector2 max = new(bounds3.max.x, bounds3.max.z);
        Vector2 center = (min + max) * 0.5f;
        Vector2 radius = new(
            Math.Max((max.x - min.x) * 0.5f + padding, padding),
            Math.Max((max.y - min.y) * 0.5f + padding, padding));

        GL.Begin(GL.TRIANGLES);
        GL.Color(Color.black);

        Vector2 centerPixel = terrainBounds.WorldToPixel(center, width, height, config.RoadMaskFlipY);

        int samples = config.RoadMaskNodeSamples;
        for (int i = 0; i < samples; i++)
        {
            Vector2 first = SampleEllipse(center, radius, i / (float)samples);
            Vector2 second = SampleEllipse(center, radius, (i + 1) / (float)samples);
            Vector2 firstPixel = terrainBounds.WorldToPixel(first, width, height, config.RoadMaskFlipY);
            Vector2 secondPixel = terrainBounds.WorldToPixel(second, width, height, config.RoadMaskFlipY);

            GL.Vertex3(centerPixel.x, centerPixel.y, 0f);
            GL.Vertex3(firstPixel.x, firstPixel.y, 0f);
            GL.Vertex3(secondPixel.x, secondPixel.y, 0f);
        }

        GL.End();
    }

    private static bool TryGetBuildingLocalBounds(EntityManager entityManager, Entity prefabEntity, out Vector2 min, out Vector2 max)
    {
        min = default;
        max = default;

        if (prefabEntity == Entity.Null)
        {
            return false;
        }

        if (entityManager.HasComponent<ObjectGeometryData>(prefabEntity))
        {
            ObjectGeometryData geometry = entityManager.GetComponentData<ObjectGeometryData>(prefabEntity);
            min = new Vector2(geometry.m_Bounds.min.x, geometry.m_Bounds.min.z);
            max = new Vector2(geometry.m_Bounds.max.x, geometry.m_Bounds.max.z);
            if (IsValidBounds(min, max))
            {
                return true;
            }

            if (geometry.m_Size.x > 0.01f && geometry.m_Size.z > 0.01f)
            {
                Vector2 halfSize = new(geometry.m_Size.x * 0.5f, geometry.m_Size.z * 0.5f);
                min = -halfSize;
                max = halfSize;
                return true;
            }
        }

        if (entityManager.HasComponent<BuildingData>(prefabEntity))
        {
            BuildingData building = entityManager.GetComponentData<BuildingData>(prefabEntity);
            if (building.m_LotSize.x > 0 && building.m_LotSize.y > 0)
            {
                Vector2 halfSize = new(
                    building.m_LotSize.x * ZoneCellSizeMeters * 0.5f,
                    building.m_LotSize.y * ZoneCellSizeMeters * 0.5f);
                min = -halfSize;
                max = halfSize;
                return true;
            }
        }

        return false;
    }

    private static bool IsValidBounds(Vector2 min, Vector2 max)
    {
        return max.x - min.x > 0.01f && max.y - min.y > 0.01f;
    }

    private static void DrawRotatedQuad(
        float3 position,
        quaternion rotation,
        Vector2 min,
        Vector2 max,
        float padding,
        FoliageConfig config,
        FoliageTerrainBounds terrainBounds,
        int width,
        int height)
    {
        min -= new Vector2(padding, padding);
        max += new Vector2(padding, padding);

        Vector2 p0 = LocalToWorldXZ(position, rotation, min.x, min.y);
        Vector2 p1 = LocalToWorldXZ(position, rotation, max.x, min.y);
        Vector2 p2 = LocalToWorldXZ(position, rotation, max.x, max.y);
        Vector2 p3 = LocalToWorldXZ(position, rotation, min.x, max.y);

        DrawQuad(p0, p1, p2, p3, config, terrainBounds, width, height);
    }

    private static bool DrawAreaPolygon(
        DynamicBuffer<AreaNode> nodes,
        float padding,
        FoliageConfig config,
        FoliageTerrainBounds terrainBounds,
        int width,
        int height)
    {
        if (nodes.Length < 3)
        {
            return false;
        }

        Vector2 center = Vector2.zero;
        for (int i = 0; i < nodes.Length; i++)
        {
            center += ToWorldXZ(nodes[i].m_Position);
        }

        center /= nodes.Length;
        Vector2 centerPixel = terrainBounds.WorldToPixel(center, width, height, config.RoadMaskFlipY);

        GL.Begin(GL.TRIANGLES);
        GL.Color(Color.black);

        for (int i = 0; i < nodes.Length; i++)
        {
            Vector2 first = ExpandFromCenter(ToWorldXZ(nodes[i].m_Position), center, padding);
            Vector2 second = ExpandFromCenter(ToWorldXZ(nodes[(i + 1) % nodes.Length].m_Position), center, padding);
            Vector2 firstPixel = terrainBounds.WorldToPixel(first, width, height, config.RoadMaskFlipY);
            Vector2 secondPixel = terrainBounds.WorldToPixel(second, width, height, config.RoadMaskFlipY);

            GL.Vertex3(centerPixel.x, centerPixel.y, 0f);
            GL.Vertex3(firstPixel.x, firstPixel.y, 0f);
            GL.Vertex3(secondPixel.x, secondPixel.y, 0f);
        }

        GL.End();
        return true;
    }

    private static Vector2 ExpandFromCenter(Vector2 point, Vector2 center, float padding)
    {
        if (padding <= 0f)
        {
            return point;
        }

        Vector2 offset = point - center;
        float length = offset.magnitude;
        return length <= 0.01f ? point : center + offset * ((length + padding) / length);
    }

    private static void DrawQuad(
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        Vector2 p3,
        FoliageConfig config,
        FoliageTerrainBounds terrainBounds,
        int width,
        int height)
    {
        p0 = terrainBounds.WorldToPixel(p0, width, height, config.RoadMaskFlipY);
        p1 = terrainBounds.WorldToPixel(p1, width, height, config.RoadMaskFlipY);
        p2 = terrainBounds.WorldToPixel(p2, width, height, config.RoadMaskFlipY);
        p3 = terrainBounds.WorldToPixel(p3, width, height, config.RoadMaskFlipY);

        GL.Begin(GL.TRIANGLES);
        GL.Color(Color.black);
        GL.Vertex3(p0.x, p0.y, 0f);
        GL.Vertex3(p1.x, p1.y, 0f);
        GL.Vertex3(p2.x, p2.y, 0f);
        GL.Vertex3(p0.x, p0.y, 0f);
        GL.Vertex3(p2.x, p2.y, 0f);
        GL.Vertex3(p3.x, p3.y, 0f);
        GL.End();
    }

    private static Vector2 LocalToWorldXZ(float3 position, quaternion rotation, float localX, float localZ)
    {
        float3 world = position + math.mul(rotation, new float3(localX, 0f, localZ));
        return new Vector2(world.x, world.z);
    }

    private static Vector2 SampleEllipse(Vector2 center, Vector2 radius, float t)
    {
        float angle = t * Mathf.PI * 2f;
        return new Vector2(
            center.x + Mathf.Cos(angle) * radius.x,
            center.y + Mathf.Sin(angle) * radius.y);
    }

    private static float3 Evaluate(Bezier4x3 bezier, float t)
    {
        float u = 1f - t;
        float uu = u * u;
        float tt = t * t;
        return uu * u * bezier.a
            + 3f * uu * t * bezier.b
            + 3f * u * tt * bezier.c
            + tt * t * bezier.d;
    }

    private static Vector2 ToWorldXZ(float3 point)
    {
        return new Vector2(point.x, point.z);
    }

    private static void ExpandPair(ref Vector2 left, ref Vector2 right, float padding)
    {
        Vector2 center = (left + right) * 0.5f;
        Vector2 half = right - center;
        float length = half.magnitude;
        if (length < 0.01f)
        {
            return;
        }

        Vector2 direction = half / length;
        left = center - direction * (length + padding);
        right = center + direction * (length + padding);
    }

    private static string DescribeTexture(Texture? texture)
    {
        return texture == null
            ? "null"
            : $"{texture.name} ({texture.GetType().Name}, {texture.width}x{texture.height}, dimension={texture.dimension})";
    }

    private struct FoliageMaskCounts
    {
        public int RoadEdgeCount;
        public int TrackEdgeCount;
        public int NodeCount;
        public int BuildingCount;
        public int SurfaceAreaCount;

        public void Add(FoliageMaskCounts other)
        {
            RoadEdgeCount += other.RoadEdgeCount;
            TrackEdgeCount += other.TrackEdgeCount;
            NodeCount += other.NodeCount;
            BuildingCount += other.BuildingCount;
            SurfaceAreaCount += other.SurfaceAreaCount;
        }
    }

    private readonly struct MaskSceneState : IEquatable<MaskSceneState>
    {
        public MaskSceneState(QueryChangeState networks, QueryChangeState buildings, QueryChangeState surfaces)
        {
            Networks = networks;
            Buildings = buildings;
            Surfaces = surfaces;
        }

        public QueryChangeState Networks { get; }

        public QueryChangeState Buildings { get; }

        public QueryChangeState Surfaces { get; }

        public bool Equals(MaskSceneState other)
        {
            return Networks.Equals(other.Networks) &&
                Buildings.Equals(other.Buildings) &&
                Surfaces.Equals(other.Surfaces);
        }

        public override bool Equals(object? obj)
        {
            return obj is MaskSceneState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Networks.GetHashCode();
                hash = (hash * 397) ^ Buildings.GetHashCode();
                hash = (hash * 397) ^ Surfaces.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return $"networks:{Networks}, buildings:{Buildings}, surfaces:{Surfaces}";
        }
    }

    private readonly struct QueryChangeState : IEquatable<QueryChangeState>
    {
        public QueryChangeState(int count, int hash)
        {
            Count = count;
            Hash = hash;
        }

        public int Count { get; }

        public int Hash { get; }

        public bool Equals(QueryChangeState other)
        {
            return Count == other.Count && Hash == other.Hash;
        }

        public override bool Equals(object? obj)
        {
            return obj is QueryChangeState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Count;
                hash = (hash * 397) ^ Hash;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{Count}/{Hash}";
        }
    }
}

internal readonly struct FoliageTerrainBounds
{
    public FoliageTerrainBounds(Vector3 center, Vector3 size)
    {
        Center = center;
        Size = size;
    }

    public Vector3 Center { get; }
    public Vector3 Size { get; }

    public Vector2 WorldToPixel(Vector2 worldXZ, int width, int height, bool flipY)
    {
        float u = (worldXZ.x - Center.x + Size.x * 0.5f) / Size.x;
        float v = (worldXZ.y - Center.z + Size.z * 0.5f) / Size.z;
        if (flipY)
        {
            v = 1f - v;
        }

        return new Vector2(u * width, v * height);
    }

    public override string ToString()
    {
        return $"center={FoliageVfxApplicator.FormatVector3ForLog(Center)}, size={FoliageVfxApplicator.FormatVector3ForLog(Size)}";
    }
}
