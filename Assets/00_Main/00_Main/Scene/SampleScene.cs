using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class SampleScene : SceneBase
{
    public override async UniTask EnterScene(CancellationToken token)
    {
        await UniTask.Yield(cancellationToken: token);

        // 진입 순서: map → pool → UI HUD → Player → HUD 바인딩
        // map/pool 인스턴스화는 추후 코드로 명시 추가 예정 (씬 배치 금지)

        Hud_GameScene hud = await App.ShowHud<Hud_GameScene>(token: token);

        Player player = await App.Instantiate<Player>(token: token);
        player.transform.position = new Vector3(0, 5, 0);

        if (hud != null)
            hud.Bind(player.GetComponent<Player_ActionHandler>());
    }

    public override void ExitScene()
    {

    }
}
