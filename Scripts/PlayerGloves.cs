
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PlayerGloves : UdonSharpBehaviour
{
    public GameObject leftGlove;
    public GameObject rightGlove;

    [UdonSynced]
    private bool isEquipped = false;

    void Start()
    {
        if (Networking.LocalPlayer != null && Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, leftGlove);
            Networking.SetOwner(Networking.LocalPlayer, rightGlove);
        }

        UpdateGloveState();
    }

    public void ToggleEquipped()
    {
        if (!Networking.IsOwner(gameObject))
            return;

        SetEquipped(!isEquipped);
    }

    private void SetEquipped(bool equipped)
    {
        isEquipped = equipped;
        UpdateGloveState();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        UpdateGloveState();
    }

    private void UpdateGloveState()
    {
        bool show = isEquipped || Networking.LocalPlayer == null;
        leftGlove.SetActive(show);
        rightGlove.SetActive(show);
    }
}
