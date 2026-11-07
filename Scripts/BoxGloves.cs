
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BoxGloves : UdonSharpBehaviour
{
    public PlayerGloves playerGloves;

    public override void Interact()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null || playerGloves == null)
            return;

        Component found = local.FindComponentInPlayerObjects(playerGloves);
        if (found == null)
            return;

        ((PlayerGloves)found).ToggleEquipped();
    }
}
