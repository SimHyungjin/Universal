using System;
using System.Collections.Generic;

/// <summary>
/// AES-256 암호화 저장 대상 플레이어 데이터.
/// 프로젝트 고유 필드(재화, 진행도 등)는 이 클래스를 직접 수정하거나
/// partial class로 확장하여 추가하세요.
/// </summary>
[Serializable]
public class PlayerData
{
    public const int CurrentVersion = 1;

    public int    Version  = CurrentVersion;
    public string UserId   = string.Empty;

    // ── 사운드 설정 ──────────────────────────────
    public float  MasterVolume = 1f;
    public float  BgmVolume   = 1f;
    public float  SfxVolume   = 1f;

    // ── 언어 설정 ────────────────────────────────
    public string Language;

    // ── 게임 재화 ────────────────────────────────────
    public int Money = 0;

    // ── 튜토리얼 진행도 ─────────────────────────────
    public int TutorialStep = 0;

    // ── 공통 게임 데이터 (필요 없으면 제거) ─────────
    public List<string> OwnedItems = new();

    public long LastSavedTick;
}
