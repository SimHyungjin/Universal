using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace MapNav.Ecs
{
        [DisallowMultipleComponent]
    public sealed class NavVisualShellPool : MonoBehaviour
    {
        [SerializeField] private NavVisualShell prefab;
        [SerializeField] private Transform poolRoot;

        private readonly Dictionary<Entity, NavVisualShell> _active = new();
        private readonly Stack<NavVisualShell> _pool = new();
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

        private void Update()
        {
            if (prefab == null) return;
            if (!TryInitQuery()) { ReleaseAll(); return; }
            RefreshShells();
        }

        private void LateUpdate()
        {
            if (!_hasQuery || _active.Count == 0) return;
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
                shell.Tick(t, m, kb, deltaTime);
                ExtendMotionLock(entity, shell.RequiredMotionLockTimer, kb);
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
                LocalTransform t = _em.GetComponentData<LocalTransform>(entity);
                NavVisualShell shell = GetShell();
                shell.Bind(entity, t);
                _active.Add(entity, shell);
            }
        }

        private NavVisualShell GetShell()
        {
            NavVisualShell shell = _pool.Count > 0
                ? _pool.Pop()
                : Instantiate(prefab, poolRoot != null ? poolRoot : transform);
            shell.gameObject.SetActive(true);
            return shell;
        }

        private void ExtendMotionLock(Entity entity, float requiredMotionLockTimer, NavAgentKnockback knockback)
        {
            if (requiredMotionLockTimer <= knockback.MotionLockTimer || !_em.HasComponent<NavAgentKnockback>(entity))
                return;

            knockback.MotionLockTimer = requiredMotionLockTimer;
            _em.SetComponentData(entity, knockback);
        }

        private void Release(Entity entity)
        {
            if (!_active.TryGetValue(entity, out NavVisualShell shell)) return;
            _active.Remove(entity);
            shell.Unbind();
            shell.gameObject.SetActive(false);
            _pool.Push(shell);
        }

        private void ReleaseAll()
        {
            _releaseScratch.Clear();
            foreach (Entity e in _active.Keys) _releaseScratch.Add(e);
            for (int i = 0; i < _releaseScratch.Count; i++) Release(_releaseScratch[i]);
        }
    }
}
