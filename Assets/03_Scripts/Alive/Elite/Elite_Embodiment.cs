using UnityEngine;

// 장수 실체(현재 섹터에서만 인스턴스화)의 ECS 무관 다리.
// 피격/사망 권위는 Character_Vitals + Character_ActionHandler가 갖고, 여기서는
// 그 결과(체력)를 Elite_State(백그라운드 진실)에 미러만 한다.
// 사망 후 디스폰/수거는 Elite_Manager.ReapDeadElites가 담당한다(몸체 코루틴에 묶지 않아 누수 방지).
[DisallowMultipleComponent]
public sealed class Elite_Embodiment : MonoBehaviour
{
    public Elite_State State { get; private set; }

    private Character_Vitals _vitals;

    public void Bind(Elite_State state)
    {
        State = state;
        if (State == null)
            return;

        SyncState();

        _vitals = GetComponent<Character_Vitals>();
        if (_vitals == null)
            return;

        // 전투 스탯은 캐릭터(SO_Character_Data)에서 온다. 진영/시작체력은 Elite_State 기준.
        SO_Character_Stats stats = State.Data != null && State.Data.Character != null
            ? State.Data.Character.StatsData
            : null;
        _vitals.Configure(
            stats != null ? stats.MaxHealth : _vitals.MaxHealth,
            stats != null ? stats.Defense : 0f,
            stats != null ? stats.GaugeMax : _vitals.GaugeMax,
            State.Faction,
            State.Health);

        _vitals.OnHealthChanged += HandleHealthChanged;
    }

    private void Update()
    {
        SyncState();
    }

    private void OnDestroy()
    {
        if (_vitals != null)
            _vitals.OnHealthChanged -= HandleHealthChanged;

        if (State != null && State.Embodiment == gameObject)
            State.DetachEmbodiment();
    }

    private void SyncState()
    {
        if (State == null) return;

        State.WorldPosition = transform.position;
        State.Forward = transform.forward.sqrMagnitude > 0.0001f
            ? transform.forward.normalized
            : Vector3.forward;
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (State != null)
            State.Health = current; // Health<=0이면 Elite_Manager.ReapDeadElites가 연출 후 수거.
    }
}
