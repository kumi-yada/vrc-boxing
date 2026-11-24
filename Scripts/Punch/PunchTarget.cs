using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Attach to any object the player can punch. A target only owns its collider,
// squash animation, hit hook and last-punch readout. All fist tracking and hit
// detection runs once per frame in the scene's single PunchDetector, which
// calls NotifyPunchHit when a swing connects; LastPunch* is set just before
// hitEventReceiver / hitEventName fires.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PunchTarget : UdonSharpBehaviour
{
    public TMP_Text readout;    // optional; last punch is written here

    [Header("Hit hook")]
    public UdonSharpBehaviour hitEventReceiver;   // optional; receives hitEventName on each hit
    public string hitEventName = "OnPunchTargetHit";
    [HideInInspector] public PunchType LastPunchType = PunchType.None; // set just before the hook fires
    [HideInInspector] public float LastPunchSpeed = 0f;
    [HideInInspector] public bool LastPunchWasLeft = false;

    [Header("Squash")]
    public float squashAmount = 0.35f; // peak y/z shrink on hit; x bulges proportionally

    // per-hand latch/cooldown, driven by PunchDetector; one swing = one hit on this target
    [HideInInspector] public bool[] latched = new bool[2];
    [HideInInspector] public float[] cooldownUntil = new float[2];

    private const float SquashWidthRatio = 0.9f / 0.35f;
    private Collider hitCollider;
    private Vector3 baseScale;
    private float squash = 1f;
    private PunchDetector detector;

    void Start()
    {
        hitCollider = GetComponent<Collider>();
        baseScale = transform.localScale;
        squashAmount = Mathf.Clamp01(squashAmount);
        // typeof(userType) is not supported by UdonSharp, so find the detector
        // by GameObject name and use the generic GetComponent instead
        GameObject detectorGO = GameObject.Find("PunchDetector");
        if (detectorGO != null) detector = detectorGO.GetComponent<PunchDetector>();
        if (detector != null)
            detector.Register(this);
        else
            Debug.LogWarning("[PunchTarget] No PunchDetector in the scene; punches on " + gameObject.name + " are disabled");
    }

    void OnDestroy()
    {
        if (detector != null) detector.Unregister(this);
    }

    void Update()
    {
        AnimateSquash();
    }

    public Collider GetHitCollider() { return hitCollider; }

    // Called by PunchDetector when a swing connects with this target.
    public void NotifyPunchHit(VRCPlayerApi player, bool isLeft, PunchType type, float speed)
    {
        LastPunchType = type;
        LastPunchSpeed = speed;
        LastPunchWasLeft = isLeft;
        if (hitEventReceiver != null && !string.IsNullOrEmpty(hitEventName))
            hitEventReceiver.SendCustomEvent(hitEventName);
        squash = 0f;
        player.PlayHapticEventInHand(
            isLeft ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right,
            0.1f, Mathf.Clamp01(speed / 8f), 0.5f);
        if (readout != null)
            readout.text = string.Format("{0}  {1}  {2}  {3:0.0} m/s",
                PunchClassifier.TypeName(type), gameObject.name, isLeft ? "L" : "R", speed);
    }

    private void AnimateSquash()
    {
        if (squash >= 1f) return;
        squash = Mathf.Min(1f, squash + Time.deltaTime * 5f);
        float k = Mathf.Sin(squash * Mathf.PI); // 0 -> 1 -> 0
        transform.localScale = new Vector3(
            baseScale.x * (1f + squashAmount * SquashWidthRatio * k),
            baseScale.y * (1f - squashAmount * k),
            baseScale.z * (1f - squashAmount * k));
    }
}
