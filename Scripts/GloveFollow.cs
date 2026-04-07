
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class GloveFollow : UdonSharpBehaviour
{
    public HumanBodyBones trackedBone;

    void Update()
    {
        var player = Networking.GetOwner(gameObject);
        if (player == null)
            return;

        transform.SetPositionAndRotation(player.GetBonePosition(trackedBone), player.GetBoneRotation(trackedBone));
    }

}
