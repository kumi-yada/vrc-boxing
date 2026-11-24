
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public enum PadPosition
{
    Jab,
    Hook,
    UpperCut,
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BoxPad : UdonSharpBehaviour
{
    public BoxStation station;
    public Transform jabPosition;
    public Transform hookPosition;
    public Transform cutPosition;

    public AudioSource hitSound;
    public float minPitch = 0.8f;
    public float maxPitch = 1.2f;
    public float minVolume = 0.8f;
    public float maxVolume = 1.2f;
    public float maxForceForSound = 8f; // m/s mapping to full volume (kept for the clip source)

    [UdonSynced] private bool isActivated = false;
    [UdonSynced] private PadPosition padPosition = PadPosition.Jab;

    private bool isLeftPad = false;

    public void InitializePad(PadPosition position, bool isLeft)
    {
        isActivated = true;
        isLeftPad = isLeft;
        MovePad(position);
    }

    public void MovePad(PadPosition position)
    {
        padPosition = position;
        UpdatePadPosition();
        RequestSerialization();
    }

    public void SetPadActive(bool active)
    {
        isActivated = active;
        UpdateActiveState();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        UpdatePadPosition();
        UpdateActiveState();
    }

    private void UpdateActiveState()
    {
        gameObject.SetActive(isActivated);
    }

    private void UpdatePadPosition()
    {
        switch (padPosition)
        {
            case PadPosition.Jab:
                transform.position = jabPosition.position;
                transform.rotation = jabPosition.rotation;
                break;
            case PadPosition.UpperCut:
                transform.position = cutPosition.position;
                transform.rotation = cutPosition.rotation;
                break;
            case PadPosition.Hook:
                transform.position = hookPosition.position;
                transform.rotation = hookPosition.rotation;
                break;
        }
    }
}
