
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public enum PadPosition
{
    Jab,
    Cross,
    Hook,
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BoxPad : UdonSharpBehaviour
{

    public BoxStation station;
    public Transform jabPosition;
    public Transform hookPosition;
    public Transform cutPosition;

    [UdonSynced] private bool isInitialized = false;
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

    public void MovePad(PadPosition position)
    {
        Debug.Log("Moving " + (isLeftPad ? "left" : "right") + " pad to position: " + position);
        padPosition = position;
        UpdatePadPosition();
        RequestSerialization();
    }

    public void SetPadActive(bool active)
    {
        Debug.Log("Setting " + (isLeftPad ? "left" : "right") + " pad active: " + active);
        isActivated = active;
        UpdateActiveState();
        RequestSerialization();
    }

    private void SetPadOwnerTo(Collider collider)
    {
        var gloveOwner = Networking.GetOwner(collider.gameObject);
        Networking.SetOwner(gloveOwner, gameObject);
        isInitialized = true;
        Debug.Log("Pad initialized with owner: " + gloveOwner.displayName);
        RequestSerialization();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null || station == null) return;

        string otherName = other.gameObject.name;
        bool isLeftHit = isLeftPad && otherName == "GloveL";
        bool isRightHit = !isLeftPad && otherName == "GloveR";
        Debug.Log(gameObject.name + " triggered by " + otherName + ". Left hit: " + isLeftHit + ", Right hit: " + isRightHit);
        if (!(isLeftHit || isRightHit))
            return;

        if (!isInitialized)
        {
            SetPadOwnerTo(other);
            return;
        }

        if (!Networking.IsOwner(gameObject)) {
            Debug.Log("Not owner of pad, ignoring collision from " + Networking.LocalPlayer.displayName);
            return;
        }

        float force = CalculateHitForce(other);
        if (force > 0f)
        {
            station.PadHit(isLeftPad, force);
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
        const float facingThreshold = 0.8f;

        if (overlapping && distance > 0f && dotLeft > facingThreshold)
        {
            return distance * forceScale;
        }

        return 0f;
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
            case PadPosition.Cross:
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
