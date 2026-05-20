using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class SampleScene : SceneBase
{
    public override async UniTask EnterScene(CancellationToken token)
    {
        await UniTask.Yield(cancellationToken: token);
        
        var player = await App.Instantiate<Player>(token: token);
        player.transform.position = new Vector3(0, 5, 0);

    }

    public override void ExitScene()
    {
        
    }
}
