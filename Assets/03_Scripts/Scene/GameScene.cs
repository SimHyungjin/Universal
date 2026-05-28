using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class GameScene : SceneBase
{
    public override async UniTask EnterScene(CancellationToken token)
    {
        await UniTask.Yield(cancellationToken: token);
        
        Hud_GameScene hud = await App.ShowHud<Hud_GameScene>(token: token);
        await App.ShowOverlay<Overlay_UltimateActivate>(ct: token);
        Player player = await App.Instantiate<Player>(token: token);
        player.transform.position = new Vector3(0, 5, 0);

        if (hud != null)
            hud.Bind(player.GetComponent<Player_ActionHandler>());
    }

    public override void ExitScene()
    {

    }
}