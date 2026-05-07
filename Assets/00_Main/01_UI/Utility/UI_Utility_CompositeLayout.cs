using UnityEngine;
using UnityEngine.UI;

public class UI_Utility_CompositeLayout : LayoutGroup
{
    public void RequestRebuild() => SetDirty();

    public override void CalculateLayoutInputHorizontal() => base.CalculateLayoutInputHorizontal();
    public override void CalculateLayoutInputVertical() { }

    public override void SetLayoutHorizontal() => UpdateLayout();
    public override void SetLayoutVertical() => UpdateLayout();

    private void UpdateLayout()
    {
        int count = rectChildren.Count;
        if (count == 0) return;

        float totalWidth = rectTransform.rect.width;
        float totalHeight = rectTransform.rect.height;
        float usableWidth = totalWidth - padding.horizontal;
        float usableHeight = totalHeight - padding.vertical;

        // Child Alignment에 따른 방향 결정
        // Left 계열(UpperLeft, MiddleLeft, LowerLeft)인지 확인
        bool isLeftToRight = childAlignment == TextAnchor.UpperLeft || 
                             childAlignment == TextAnchor.MiddleLeft || 
                             childAlignment == TextAnchor.LowerLeft;

        // 시작 위치 설정
        float currentPos = isLeftToRight ? padding.left : totalWidth - padding.right;

        for (int i = 0; i < count; i++)
        {
            RectTransform child = rectChildren[i];
            UI_Utility_CompositeLayoutElement element = child.GetComponent<UI_Utility_CompositeLayoutElement>();

            float width = 0;
            if (element == null)
            {
                width = usableHeight;
            }
            else
            {
                switch (element.type)
                {
                    case UI_Utility_CompositeLayoutElement.LayoutType.AnchorFixed:
                        width = usableWidth * element.anchorRatio;
                        break;
                    case UI_Utility_CompositeLayoutElement.LayoutType.AspectFixed:
                        width = usableHeight * element.aspect;
                        break;
                    case UI_Utility_CompositeLayoutElement.LayoutType.Stretch:
                        // 방향에 따라 남은 공간 계산
                        width = isLeftToRight 
                            ? Mathf.Max(0, (totalWidth - padding.right) - currentPos)
                            : Mathf.Max(0, currentPos - padding.left);
                        break;
                }
            }

            // 배치 위치 결정
            float finalX;
            if (isLeftToRight)
            {
                finalX = currentPos;
                currentPos += width; // 오른쪽으로 이동
            }
            else
            {
                currentPos -= width; // 왼쪽으로 이동
                finalX = currentPos;
            }

            // 가로/세로 배치 적용
            SetChildAlongAxis(child, 0, finalX, width);
            
            // 세로 정렬(Y축)은 Alignment의 상/중/하 설정을 따르도록 GetStartOffset 활용
            float startY = GetStartOffset(1, usableHeight); 
            SetChildAlongAxis(child, 1, startY, usableHeight);
        }
    }
}