
using BestHTTP.SecureProtocol.Org.BouncyCastle.Ocsp;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BoxGloves : UdonSharpBehaviour
{
    public GameObject leftGlove;
    public GameObject rightGlove;

    [UdonSynced]
    private bool isEquipped = false;

    private bool isEditor = false;

    void Start()
    {
        isEditor = Networking.LocalPlayer == null;
        leftGlove.SetActive(isEditor);
        rightGlove.SetActive(isEditor);
    }

    public override void Interact()
    {
        if (isEquipped) {
            if (Networking.IsOwner(gameObject))
            {
                SetEquipped(false);
            }
            return;
        }

        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);

        SetEquipped(true);
    }

    private void SetEquipped(bool equipped)
    {
        Debug.Log("Setting gloves equipped: " + equipped);
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
        VRCPlayerApi owner = Networking.GetOwner(gameObject);
        if (!isEquipped || owner == null)
        {
            Debug.Log("Gloves unequipped or no owner");
            leftGlove.SetActive(false);
            rightGlove.SetActive(false);
        }
        else
        {
            Debug.Log("Gloves equipped");
            Networking.SetOwner(owner, leftGlove.gameObject);
            Networking.SetOwner(owner, rightGlove.gameObject);
            leftGlove.SetActive(true);
            rightGlove.SetActive(true);
        }
    }
}
