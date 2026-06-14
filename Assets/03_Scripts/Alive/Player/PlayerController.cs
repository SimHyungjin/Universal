using MapNav.Ecs;
using UnityEngine;

// '플레이어'는 캐릭터에 구워진 속성이 아니라, 자율(AI 기본) 캐릭터 하나에 입력·카메라·HUD·진영을
// 주입하는 빙의(possession)다. 모든 캐릭터 프리팹은 동일하게 풀-베이킹(자율)이고, 여기서 커맨드 소스를
// 플레이어 입력으로 바꾸고 AI(Elite_Brain)·nav를 끄면 그 캐릭터가 플레이어가 된다.
// Release()로 소스를 AI로 되돌리면 automode가 된다(state가 바인드돼 있으면 AI가 다시 조종).
public sealed class PlayerController
{
    public static PlayerController Instance { get; private set; }

    private readonly Player_InputCommandSource _playerInput = new();

    private GameObject _character;
    private Character_ActionHandler _actionHandler;
    private Character_Vitals _vitals;
    private Character_AttackController _attackController;
    private Character_CommandSource _aiSource;
    private Elite_Brain _brain;
    private MapNavMonoAgent _navAgent;
    private Character_PlayerControl _marker;

    public GameObject Character => _character;
    public Character_ActionHandler ActionHandler => _actionHandler;
    public Character_Vitals Vitals => _vitals;
    public Transform Transform => _character != null ? _character.transform : null;

    // 이 핸들러가 지금 플레이어가 빙의한 캐릭터인가. 플레이어 시점 juice(카메라 셰이크·전역 슬로모·컷인)는
    // "내가(로컬 플레이어가) 때렸을 때"만 나와야 한다 — 적/아군 AI 공격은 화면을 흔들거나 시간을 멈추지 않는다.
    public static bool IsLocalPlayer(Character_ActionHandler handler)
        => Instance != null && handler != null && Instance.ActionHandler == handler;

    // 카메라 추종 오프셋/뷰(구 Player_Actor.Start 값 이관).
    private static readonly Vector3 CameraOffset = new(5f, 11f, -5f);
    private static readonly Vector3 CameraEuler = new(55f, -45f, 0f);
    private const float CameraOrthographicSize = 7f;

    public void Possess(GameObject character, SO_Character_Data data)
    {
        if (character == null)
            return;

        Release();

        _character = character;
        _actionHandler = character.GetComponent<Character_ActionHandler>();
        _vitals = character.GetComponent<Character_Vitals>();
        _attackController = character.GetComponent<Character_AttackController>();
        _aiSource = character.GetComponent<Elite_AICommandSource>();
        _brain = character.GetComponent<Elite_Brain>();
        _navAgent = character.GetComponent<MapNavMonoAgent>();

        // 캐릭터 데이터(이동/애니/로드아웃) + 진영(Ally)·스탯 주입. 진영 주입으로 FactionResolved 게이트 해제.
        _actionHandler?.SetCharacterData(data, clearEquippedLoadout: true);
        _actionHandler?.ConfigureVitals(NavFaction.Ally);

        // 자율 드라이버 정지: 플레이어가 직접 조종하는 동안 AI 두뇌·nav가 끼어들지 않게.
        if (_brain != null) _brain.enabled = false;
        if (_navAgent != null) _navAgent.enabled = false;

        // 커맨드 소스를 플레이어 입력으로 교체(기본은 구워진 Elite_AICommandSource).
        _actionHandler?.SetCommandSource(_playerInput);
        _attackController?.SetDrivesCameraFollowAlignment(true);

        if (_marker == null)
            _marker = character.GetComponent<Character_PlayerControl>();
        if (_marker == null)
            _marker = character.AddComponent<Character_PlayerControl>();

        Instance = this;

        BindInputAndCamera();
    }

    // 빙의 해제(또는 automode): 소스를 AI로 되돌리고 두뇌·nav를 재활성, 입력·카메라·마커 정리.
    public void Release()
    {
        if (_character != null)
        {
            UnbindInputAndCamera();

            _actionHandler?.SetCommandSource(_aiSource);
            _attackController?.SetDrivesCameraFollowAlignment(false);
            if (_brain != null) _brain.enabled = true;
            if (_navAgent != null) _navAgent.enabled = true;

            if (_marker != null)
                Object.Destroy(_marker);
        }

        if (Instance == this)
            Instance = null;

        _character = null;
        _actionHandler = null;
        _vitals = null;
        _attackController = null;
        _aiSource = null;
        _brain = null;
        _navAgent = null;
        _marker = null;
    }

    private void BindInputAndCamera()
    {
        if (_character == null)
            return;

        App.SetInput<InputActions_Move, InputActions_Combat, InputActions_Camera>();

        Transform tr = _character.transform;
        App.SetCameraView(tr.position + CameraOffset, CameraEuler, orthographicSize: CameraOrthographicSize);
        App.SetCameraFollow(tr, CameraOffset, snap: true);
        App.SetCombatCameraMode(CombatCameraMode.Tactical, true);
    }

    private void UnbindInputAndCamera()
    {
        App.RemoveInput<InputActions_Move>();
        App.RemoveInput<InputActions_Combat>();
        App.RemoveInput<InputActions_Camera>();
        if (_character != null)
            App.ClearCameraFollow(_character.transform);
    }
}
