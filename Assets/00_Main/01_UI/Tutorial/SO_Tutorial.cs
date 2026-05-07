using UnityEngine;

[CreateAssetMenu(fileName = "SO_Tutorial", menuName = "Game/Tutorial")]
public class SO_Tutorial : ScriptableObject
{
    public string Title;
    public Sprite Image;
    [TextArea(3, 8)]
    public string Description;
}
