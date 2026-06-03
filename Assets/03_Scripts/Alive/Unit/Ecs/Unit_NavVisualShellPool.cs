using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

namespace MapNav.Ecs
{
    [DisallowMultipleComponent]
    public sealed class Unit_NavVisualShellPool : MonoBehaviour
    {
        [FormerlySerializedAs("prefab")]
        [FormerlySerializedAs("enemyPrefab")]
        [FormerlySerializedAs("fallbackEnemyPrefab")]
        [SerializeField] private Unit_NavVisualShell fallbackPrefab;
        [SerializeField] private Transform poolRoot;

        private readonly Dictionary<Entity, Unit_NavVisualShell> _active = new();
        private readonly Dictionary<Unit_NavVisualShell, Stack<Unit_NavVisualShell>> _pools = new();
        private readonly Dictionary<Unit_NavVisualShell, Unit_NavVisualShell> _sourcePrefabByShell = new();
        private readonly List<Entity> _releaseScratch = new();
        private EntityQuery _agentQuery;
        private EntityManager _em;
        private World _world;
        private bool _hasQuery;

        private void OnEnable() => TryInitQuery();

        private void OnDisable()
        {
            ReleaseAll();
            DisposeQuery();
        }

        private void LateUpdate()
        {
            if (fallbackPrefab == null && NavRuntimeBootstrap.Instance == null) return;
            if (!TryInitQuery()) { ReleaseAll(); return; }

            RefreshShells();
            if (_active.Count == 0) return;

            float deltaTime = Time.deltaTime;
            _releaseScratch.Clear();

            foreach (KeyValuePair<Entity, Unit_NavVisualShell> pair in _active)
            {
                Entity entity = pair.Key;
                Unit_NavVisualShell shell = pair.Value;
                if (!_em.HasComponent<LocalTransform>(entity))
                {
                    shell.TickIdle();
                    _releaseScratch.Add(entity);
                    continue;
                }

                LocalTransform t = _em.GetComponentData<LocalTransform>(entity);
                NavAgentMotion m = _em.HasComponent<NavAgentMotion>(entity)
                    ? _em.GetComponentData<NavAgentMotion>(entity)
                    : default;
                NavAgentKnockback kb = _em.HasComponent<NavAgentKnockback>(entity)
                    ? _em.GetComponentData<NavAgentKnockback>(entity)
                    : default;
                NavAgentLaunch launch = _em.HasComponent<NavAgentLaunch>(entity)
                    ? _em.GetComponentData<NavAgentLaunch>(entity)
                    : default;
                NavAgentDeath death = _em.HasComponent<NavAgentDeath>(entity)
                    ? _em.GetComponentData<NavAgentDeath>(entity)
                    : default;
                NavAgentAttack attack = _em.HasComponent<NavAgentAttack>(entity)
                    ? _em.GetComponentData<NavAgentAttack>(entity)
                    : default;
                NavAgentHealth health = _em.HasComponent<NavAgentHealth>(entity)
                    ? _em.GetComponentData<NavAgentHealth>(entity)
                    : default;
                shell.Tick(t, m, kb, launch, death, attack, health, deltaTime);
            }

            for (int i = 0; i < _releaseScratch.Count; i++)
                Release(_releaseScratch[i], true);
        }

        private bool TryInitQuery()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return false;
            if (_world == w && _hasQuery) return true;
            DisposeQuery();
            _world = w;
            _em = w.EntityManager;
            _agentQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<NavAgentSettings>(),
                ComponentType.ReadOnly<NavAgentMotion>(),
                ComponentType.ReadOnly<NavAgentFaction>(),
                ComponentType.ReadOnly<LocalTransform>());
            _hasQuery = true;
            return true;
        }

        private void DisposeQuery()
        {
            if (!_hasQuery) return;
            _hasQuery = false;
            if (_world == null || !_world.IsCreated) return;

            try
            {
                _agentQuery.Dispose();
            }
            catch (System.NullReferenceException)
            {
                // Unity can invalidate query internals while leaving World.IsCreated true during play-mode shutdown.
            }
        }

        private void RefreshShells()
        {
            _releaseScratch.Clear();
            foreach (KeyValuePair<Entity, Unit_NavVisualShell> pair in _active)
            {
                if (!_em.Exists(pair.Key) || !_em.HasComponent<LocalTransform>(pair.Key))
                    _releaseScratch.Add(pair.Key);
            }
            for (int i = 0; i < _releaseScratch.Count; i++) Release(_releaseScratch[i], true);

            using NativeArray<Entity> entities = _agentQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (_active.ContainsKey(entity)) continue;

                NavFaction faction = _em.HasComponent<NavAgentFaction>(entity)
                    ? _em.GetComponentData<NavAgentFaction>(entity).Faction
                    : NavFaction.Enemy;
                SO_Unit_Data data = NavRuntimeBootstrap.Instance != null
                    ? NavRuntimeBootstrap.Instance.GetUnitData(entity)
                    : null;

                LocalTransform t = _em.GetComponentData<LocalTransform>(entity);
                NavAgentAttackProfile profile = _em.HasComponent<NavAgentAttackProfile>(entity)
                    ? _em.GetComponentData<NavAgentAttackProfile>(entity)
                    : default;
                Unit_NavVisualShell shell = GetShell(data);
                if (shell == null) continue;

                shell.Bind(entity, faction, t, profile, data != null ? data.AnimationData : null);
                _active.Add(entity, shell);
            }
        }

        private Unit_NavVisualShell GetShell(SO_Unit_Data data)
        {
            Unit_NavVisualShell prefab = GetPrefab(data);
            if (prefab == null) return null;

            Stack<Unit_NavVisualShell> pool = GetPool(prefab);
            Unit_NavVisualShell shell = pool.Count > 0
                ? pool.Pop()
                : Instantiate(prefab, poolRoot != null ? poolRoot : transform);
            _sourcePrefabByShell[shell] = prefab;
            shell.gameObject.SetActive(true);
            return shell;
        }

        private void Release(Entity entity, bool forgetData)
        {
            if (!_active.TryGetValue(entity, out Unit_NavVisualShell shell)) return;
            _active.Remove(entity);
            if (forgetData)
                NavRuntimeBootstrap.Instance?.ForgetUnitData(entity);

            shell.Unbind();
            shell.gameObject.SetActive(false);

            if (_sourcePrefabByShell.TryGetValue(shell, out Unit_NavVisualShell sourcePrefab))
                GetPool(sourcePrefab).Push(shell);
        }

        private void ReleaseAll()
        {
            _releaseScratch.Clear();
            foreach (Entity e in _active.Keys) _releaseScratch.Add(e);
            for (int i = 0; i < _releaseScratch.Count; i++) Release(_releaseScratch[i], false);
        }

        private Unit_NavVisualShell GetPrefab(SO_Unit_Data data)
        {
            Unit_NavVisualShell dataPrefab = data != null ? data.VisualPrefab : null;
            if (dataPrefab != null)
                return dataPrefab;
            return fallbackPrefab;
        }

        private Stack<Unit_NavVisualShell> GetPool(Unit_NavVisualShell prefab)
        {
            if (!_pools.TryGetValue(prefab, out Stack<Unit_NavVisualShell> pool))
            {
                pool = new Stack<Unit_NavVisualShell>();
                _pools.Add(prefab, pool);
            }
            return pool;
        }
    }
}
