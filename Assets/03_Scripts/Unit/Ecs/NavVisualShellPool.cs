using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

namespace MapNav.Ecs
{
    [DisallowMultipleComponent]
    public sealed class NavVisualShellPool : MonoBehaviour
    {
        [FormerlySerializedAs("prefab")]
        [SerializeField] private NavVisualShell enemyPrefab;
        [SerializeField] private NavVisualShell allyPrefab;
        [SerializeField] private Transform poolRoot;

        private readonly Dictionary<Entity, NavVisualShell> _active = new();
        private readonly Stack<NavVisualShell> _enemyPool = new();
        private readonly Stack<NavVisualShell> _allyPool = new();
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
            if (enemyPrefab == null && allyPrefab == null) return;
            if (!TryInitQuery()) { ReleaseAll(); return; }

            RefreshShells();
            if (_active.Count == 0) return;

            float deltaTime = Time.deltaTime;
            _releaseScratch.Clear();

            foreach (KeyValuePair<Entity, NavVisualShell> pair in _active)
            {
                Entity entity = pair.Key;
                NavVisualShell shell = pair.Value;
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
                NavAgentDeath death = _em.HasComponent<NavAgentDeath>(entity)
                    ? _em.GetComponentData<NavAgentDeath>(entity)
                    : default;
                NavAgentAttack attack = _em.HasComponent<NavAgentAttack>(entity)
                    ? _em.GetComponentData<NavAgentAttack>(entity)
                    : default;
                NavAgentHealth health = _em.HasComponent<NavAgentHealth>(entity)
                    ? _em.GetComponentData<NavAgentHealth>(entity)
                    : default;
                NavAgentLaunch launch = _em.HasComponent<NavAgentLaunch>(entity)
                    ? _em.GetComponentData<NavAgentLaunch>(entity)
                    : default;
                shell.Tick(t, m, kb, death, attack, health, launch, deltaTime);
                if (_em.HasComponent<NavAgentLaunch>(entity) && launch.VisualYOffset != shell.VisualYOffset)
                {
                    launch.VisualYOffset = shell.VisualYOffset;
                    _em.SetComponentData(entity, launch);
                }
            }

            for (int i = 0; i < _releaseScratch.Count; i++)
                Release(_releaseScratch[i]);
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
            foreach (KeyValuePair<Entity, NavVisualShell> pair in _active)
            {
                if (!_em.Exists(pair.Key) || !_em.HasComponent<LocalTransform>(pair.Key))
                    _releaseScratch.Add(pair.Key);
            }
            for (int i = 0; i < _releaseScratch.Count; i++) Release(_releaseScratch[i]);

            using NativeArray<Entity> entities = _agentQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (_active.ContainsKey(entity)) continue;
                NavFaction faction = _em.HasComponent<NavAgentFaction>(entity)
                    ? _em.GetComponentData<NavAgentFaction>(entity).Faction
                    : NavFaction.Enemy;
                LocalTransform t = _em.GetComponentData<LocalTransform>(entity);
                NavAgentAttackProfile profile = _em.HasComponent<NavAgentAttackProfile>(entity)
                    ? _em.GetComponentData<NavAgentAttackProfile>(entity)
                    : default;
                NavVisualShell shell = GetShell(faction);
                if (shell == null) continue;

                shell.Bind(entity, faction, t, profile);
                _active.Add(entity, shell);
            }
        }

        private NavVisualShell GetShell(NavFaction faction)
        {
            Stack<NavVisualShell> pool = GetPool(faction);
            NavVisualShell prefab = GetPrefab(faction);
            if (prefab == null) return null;

            NavVisualShell shell = pool.Count > 0
                ? pool.Pop()
                : Instantiate(prefab, poolRoot != null ? poolRoot : transform);
            shell.gameObject.SetActive(true);
            return shell;
        }

        private void Release(Entity entity)
        {
            if (!_active.TryGetValue(entity, out NavVisualShell shell)) return;
            _active.Remove(entity);
            NavFaction faction = shell.Faction;
            shell.Unbind();
            shell.gameObject.SetActive(false);
            GetPool(faction).Push(shell);
        }

        private void ReleaseAll()
        {
            _releaseScratch.Clear();
            foreach (Entity e in _active.Keys) _releaseScratch.Add(e);
            for (int i = 0; i < _releaseScratch.Count; i++) Release(_releaseScratch[i]);
        }

        private NavVisualShell GetPrefab(NavFaction faction)
        {
            if (faction == NavFaction.Ally)
                return allyPrefab != null ? allyPrefab : enemyPrefab;
            return enemyPrefab != null ? enemyPrefab : allyPrefab;
        }

        private Stack<NavVisualShell> GetPool(NavFaction faction)
            => faction == NavFaction.Ally ? _allyPool : _enemyPool;
    }
}
