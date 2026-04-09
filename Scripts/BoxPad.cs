
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
    public float maxForceForSound = 0.5f;

    [UdonSynced] private bool isActivated = false;
    [UdonSynced] private PadPosition padPosition = PadPosition.Jab;

    private bool isLeftPad = false;
    private Collider padCollider;
    private const float forceScale = 100f;

    void Start()
    {
        padCollider = GetComponent<Collider>();
    }

    public void InitializePad(PadPosition position, bool isLeft)
    {
        isActivated = true;
        isLeftPad = isLeft;
        MovePad(position);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null || station == null) return;

        var gloveOwner = Networking.GetOwner(other.gameObject);
        if (gloveOwner != Networking.LocalPlayer)
            return;

        string otherName = other.gameObject.name;
        bool isLeftHit = isLeftPad && otherName == "GloveL";
        bool isRightHit = !isLeftPad && otherName == "GloveR";
        if (!(isLeftHit || isRightHit))
            return;

        if (!station.isInitialized)
        {
            station.PrepareGame(gloveOwner);
            return;
        }

        if (!Networking.IsOwner(gameObject)) {
            return;
        }

        float force = CalculateHitForce(other);
        if (force > 0f)
        {
            station.PadHit(isLeftPad, force);

            if (hitSound != null)
            {
                float pitch = Random.Range(minPitch, maxPitch);
                float volume = Mathf.Lerp(minVolume, maxVolume, force / maxForceForSound);
                hitSound.pitch = pitch;
                hitSound.volume = volume;
                hitSound.Play();
            }
        }
    }

    private float CalculateHitForce(Collider other)
    {
        Vector3 direction;
        float distance;
        bool overlapping = Physics.ComputePenetration(
            other, other.transform.position, other.transform.rotation,
            padCollider, transform.position, transform.rotation,
            out direction, out distance
        );

        // convert the penetration direction (world space) into the pad's local space
        Vector3 localDirection = transform.InverseTransformDirection(direction);
        float dotLeft = Vector3.Dot(localDirection.normalized, Vector3.left);
        Debug.Log("Hit force calculation. Direction (local): " + localDirection + ", Distance: " + distance + ", DotLeft: " + dotLeft);

        // Only count hits where the local direction is facing the negative X axis (left).
        // Use dot product with Vector3.left; require a reasonable alignment threshold.
        const float facingThreshold = 0.5f;

        if (overlapping && distance > 0f && dotLeft > facingThreshold)
        {
            return distance * forceScale;
        }

        return 0f;
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
