using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Image의 투명 영역이 레이캐스트에 걸리지 않도록 alphaHitTestMinimumThreshold를 설정한다.
/// 텍스처에 Read/Write가 꺼져 있으면 경고만 출력하고 기본 동작(사각형)으로 유지된다.
/// </summary>
[RequireComponent(typeof(Image))]
public class UI_Utility_AlphaRaycastTarget : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float threshold = 0.1f;

    private void Awake()
    {
        if (threshold <= 0f) return;

        var image = GetComponent<Image>();
        var texture = image.sprite != null ? image.sprite.texture : null;

        if (texture != null && !texture.isReadable)
        {
            Debug.LogWarning(
                $"[{nameof(UI_Utility_AlphaRaycastTarget)}] '{name}' : " +
                "Sprite texture Read/Write Enabled가 꺼져 있어 alpha hit test가 동작하지 않을 수 있습니다. " +
                "Sprite Import Settings에서 Read/Write Enabled를 켜주세요.",
                this);
        }

        try
        {
            image.alphaHitTestMinimumThreshold = threshold;
        }
        catch (System.InvalidOperationException)
        {
            Debug.LogWarning(
                $"[{nameof(UI_Utility_AlphaRaycastTarget)}] '{name}' : " +
                "텍스처에 Read/Write가 꺼져 있어 알파 히트 테스트를 적용할 수 없습니다. " +
                "Sprite Import Settings에서 Read/Write Enabled를 체크하세요.",
                this);
        }
    }
}
