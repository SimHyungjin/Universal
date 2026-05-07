using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class SampleScene : SceneBase
{
    public override async UniTask EnterScene(CancellationToken token)
    {
        await UniTask.Yield(cancellationToken: token);

        App.SetCameraView(
            new Vector3(5f, 11f, -5f),
            new Vector3(55f, -45f, 0f),
            orthographicSize: 15f);

        var player = await App.Instantiate<Player>(token: token);
        player.transform.position = new Vector3(0, 5, 0);

    }

    public override void ExitScene()
    {
        
    }
}
