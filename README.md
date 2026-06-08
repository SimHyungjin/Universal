# 무쌍 액션 RPG (개인 포트폴리오 프로젝트)

> 페이트 엑스텔라 링크류 무쌍 액션을 목표로 단독 설계·구현 중인 Unity 개인 프로젝트입니다.

**Engine** Unity 6 · C# · Unity DOTS (ECS)  
**Period** 2026.05 ~  
**Role** 단독 개발 (아키텍처·전투·맵·UI 전체)

---

## 핵심 구현

### 1. ECS + MonoBehaviour 하이브리드 아키텍처

| 유닛 종류 | 구현 방식 | 이유 |
|---|---|---|
| 잡몹 (수백 체) | Unity DOTS ECS Entity | Update 직접 호출 없이 SystemBase 병렬 처리 |
| 장수 (보스급) | MonoBehaviour + AI 입력 주입 | 복잡한 공격 패턴, 플레이어 전투 스택 100% 재사용 |

- **진영 확정 게이트** : `Vitals.FactionResolved` 플래그가 set되기 전까지 ECS 타겟 선정에서 제외 → 장수 초기화 전 잡몹 오인 어그로 방지

### 2. 근접·원거리·장판 전투 시스템

- **AttackHitEmitter** : `pose(origin + forward)` 기반 단일 히트 판정 출처. GameObject(장수·파괴물)와 ECS 잡몹을 한 번에 판정해 이원화 문제 해소
- **SO_Attack_Data** 기반 데이터 주도 설계
  - 근접 : 타이밍·VFX·판정 범위
  - 발사체 : 멀티샷·연사·관통·최대 횟수
  - 장판 : 번개형(적 발밑 즉발) / 거리형 / 투척폭발형(발사체→장판) 3패턴
- **Projectile_Hitbox / Field_Hitbox** : `IPoolable + Addressables` 풀 스폰, 히트스톱·흡혈·게이지·카메라 컷인 공통 처리

### 3. 절차 생성 다중 섹터 + 실시간 전환

- **SectorGenerator** : by-construction 성장 방식으로 항상 연결된 섹터 그래프를 보장 (분리된 섬 없음)
- **NavBlob swap** : 섹터 진입 시 전체 재베이크 없이 ECS Nav 싱글톤만 교체. 이전 섹터 Entity 파괴 + Pool 반납 자동화
- **BackgroundSimulator** : 비활성 섹터를 DPS 추상 모델로 매 틱 갱신 — 실체화 없이 점령 상태 유지
- **미니맵** : `SectorGenerator`가 `MinimapModel` emit → `Texture2D` 래스터화, 플레이어 마커 러프 수렴

### 4. 제로섬 거점 점령 시스템

- 잡몹 사망 = 파괴 대신 **반대 진영으로 부활** → 유닛 수 합산 항상 일정 (제로섬)
- **BackgroundBattle** : 링크(연결 컴포넌트) 기반으로 점령·변이·전선 흐름 결정
- `SO_SectorBattle_Settings` : 점령 속도·Power 계수 전부 디자이너 튜닝 가능
- **침식도 다이얼(0~9)** : 시작 보드 초기 진영 분포 설정

---

## 트러블슈팅

### 장수 진입 시 전체 잡몹 오인 어그로

**문제** ECS 잡몹 수백 체가 장수(MonoBehaviour)를 타겟으로 삼을 때, 진영 주입이 완료되기 전 장수가 ECS 타겟 풀에 노출되어 전체 잡몹이 아군 장수에게 돌진하는 현상 발생

**원인** 장수 MonoBehaviour가 먼저 활성화되어 ECS 타겟 풀에 등록되지만, 진영(Faction) 데이터 주입은 이후 비동기로 완료됨. ECS SystemBase가 진영 무관하게 최근접 타겟을 선택해 아군 장수에게 어그로

**해결** `Vitals.FactionResolved` 플래그 도입. 진영 주입 완료 전까지 ECS 타겟 선정에서 제외. 초기화 완료 시 플래그 set → 이후 정상 어그로. 이후 새 캐릭터 추가 시 동일 게이트만 따르면 동일하게 보장

---

## 기술 스택

```
Unity 6 / C# / Unity DOTS (ECS, Collections, Burst)
Unity AI Navigation (NavMeshAgent)
Addressables · IPoolable Object Pool
ScriptableObject 기반 데이터 아키텍처
```

---

## 폴더 구조

```
Assets/
├── 00_Main/          # 재사용 가능한 코어 (Pool, Formula, Utility)
├── 01_Assets/        # 외부 에셋 (VFX, 모델, 텍스처)
├── 03_Scripts/       # 프로젝트 도메인 로직
│   ├── Alive/        # 캐릭터 (Player, Elite, AI)
│   ├── Combat/       # 전투 (HitEmitter, Projectile, Field)
│   ├── Sector/       # 섹터·맵·미니맵
│   └── UI/           # HUD, Minimap
├── 04_Data/          # ScriptableObject 데이터
└── 05_Prefabs/       # 프리팹
```
