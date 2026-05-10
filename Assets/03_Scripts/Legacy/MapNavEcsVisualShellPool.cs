using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavEcsVisualShellPool : MonoBehaviour
{
    [SerializeField] private MapNavEcsVisualShell prefab;
    [SerializeField] private Transform poolRoot;
    [SerializeField] private Camera cullingCamera;
    [SerializeField] private float visibleDistance = 60f;
    [SerializeField] private float visibleDistanceHysteresis = 5f;
    [SerializeField] private int maxVisibleShells = 256;
    [SerializeField] private float refreshInterval = 0.15f;

    private readonly Dictionary<Entity, MapNavEcsVisualShell> _active = new Dictionary<Entity, MapNavEcsVisualShell>();
    private readonly Stack<MapNavEcsVisualShell> _pool = new Stack<MapNavEcsVisualShell>();
    private readonly List<Entity> _releaseScratch = new List<Entity>();
    private EntityQuery _agentQuery;
    private EntityManager _entityManager;
    private World _world;
    private bool _hasAgentQuery;
    private float _nextRefreshTime;

    private void OnEnable()
    {
        TryInitializeQuery();
    }

    private void OnDisable()
    {
        ReleaseAll();
        if (_hasAgentQuery)
        {
            _agentQuery.Dispose();
            _hasAgentQuery = false;
        }
    }

    private void Update()
    {
        if (prefab == null)
            return;

        if (Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, refreshInterval);
        if (!TryInitializeQuery())
        {
            ReleaseAll();
            return;
        }

        RefreshShells();
    }

    private void LateUpdate()
    {
        if (!_hasAgentQuery || _active.Count == 0)
            return;

        float deltaTime = Time.deltaTime;
        _releaseScratch.Clear();

        foreach (KeyValuePair<Entity, MapNavEcsVisualShell> pair in _active)
        {
            Entity entity = pair.Key;
            MapNavEcsVisualShell shell = pair.Value;

            if (!_entityManager.HasComponent<LocalTransform>(entity))
            {
                shell.TickIdle();
                _releaseScratch.Add(entity);
                continue;
            }

            LocalTransform ecsTransform = _entityManager.GetComponentData<LocalTransform>(entity);
            MapNavEcsMotionState motion = _entityManager.HasComponent<MapNavEcsMotionState>(entity)
                ? _entityManager.GetComponentData<MapNavEcsMotionState>(entity)
                : default;

            shell.Tick(ecsTransform, motion, deltaTime);
        }

        for (int i = 0; i < _releaseScratch.Count; i++)
            Release(_releaseScratch[i]);
    }

    private bool TryInitializeQuery()
    {
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld == null || !defaultWorld.IsCreated)
            return false;

        if (_world == defaultWorld && _hasAgentQuery)
            return true;

        if (_hasAgentQuery)
        {
            _agentQuery.Dispose();
            _hasAgentQuery = false;
        }

        _world = defaultWorld;
        _entityManager = _world.EntityManager;
        _agentQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<MapNavEcsAgent>(),
            ComponentType.ReadOnly<MapNavEcsMotionState>(),
            ComponentType.ReadOnly<LocalTransform>());
        _hasAgentQuery = true;
        return true;
    }

    private void RefreshShells()
    {
        Vector3 origin = cullingCamera != null ? cullingCamera.transform.position : transform.position;
        float showDistanceSq = visibleDistance * visibleDistance;
        float hideDistance = visibleDistance + Mathf.Max(0f, visibleDistanceHysteresis);
        float hideDistanceSq = hideDistance * hideDistance;
        int maxShells = Mathf.Max(0, maxVisibleShells);
        int spawned = 0;

        _releaseScratch.Clear();
        foreach (KeyValuePair<Entity, MapNavEcsVisualShell> pair in _active)
        {
            if (!_entityManager.Exists(pair.Key) || !_entityManager.HasComponent<LocalTransform>(pair.Key))
            {
                _releaseScratch.Add(pair.Key);
                continue;
            }

            LocalTransform ecsTransform = _entityManager.GetComponentData<LocalTransform>(pair.Key);
            if (((Vector3)ecsTransform.Position - origin).sqrMagnitude > hideDistanceSq || spawned >= maxShells)
                _releaseScratch.Add(pair.Key);
            else
                spawned++;
        }

        for (int i = 0; i < _releaseScratch.Count; i++)
            Release(_releaseScratch[i]);

        using NativeArray<Entity> entities = _agentQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < entities.Length && spawned < maxShells; i++)
        {
            Entity entity = entities[i];
            if (_active.ContainsKey(entity))
                continue;

            LocalTransform ecsTransform = _entityManager.GetComponentData<LocalTransform>(entity);
            if (((Vector3)ecsTransform.Position - origin).sqrMagnitude > showDistanceSq)
                continue;

            MapNavEcsVisualShell shell = GetShell();
            shell.Bind(entity, ecsTransform);
            _active.Add(entity, shell);
            spawned++;
        }
    }

    private MapNavEcsVisualShell GetShell()
    {
        MapNavEcsVisualShell shell = _pool.Count > 0
            ? _pool.Pop()
            : Instantiate(prefab, poolRoot != null ? poolRoot : transform);
        shell.gameObject.SetActive(true);
        return shell;
    }

    private void Release(Entity entity)
    {
        if (!_active.TryGetValue(entity, out MapNavEcsVisualShell shell))
            return;

        _active.Remove(entity);
        shell.Unbind();
        shell.gameObject.SetActive(false);
        _pool.Push(shell);
    }

    private void ReleaseAll()
    {
        _releaseScratch.Clear();
        foreach (Entity entity in _active.Keys)
            _releaseScratch.Add(entity);

        for (int i = 0; i < _releaseScratch.Count; i++)
            Release(_releaseScratch[i]);
    }
}
