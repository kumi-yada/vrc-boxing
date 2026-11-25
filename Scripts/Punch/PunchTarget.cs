using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Attach to any object the player can punch. A target only owns its collider,
// hit hook and last-punch readout. All fist tracking and hit
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

    // per-hand latch/cooldown, driven by PunchDetector; one swing = one hit on this target
    [HideInInspector] public bool[] latched = new bool[2];
    [HideInInspector] public float[] cooldownUntil = new float[2];

    private Collider hitCollider;
    private PunchDetector detector;

    void Start()
    {
        hitCollider = GetComponent<Collider>();
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

    public Collider GetHitCollider() { return hitCollider; }

    // Called by PunchDetector when a swing connects with this target.
    public void NotifyPunchHit(VRCPlayerApi player, bool isLeft, PunchType type, float speed)
    {
        LastPunchType = type;
        LastPunchSpeed = speed;
        LastPunchWasLeft = isLeft;
        if (hitEventReceiver != null && !string.IsNullOrEmpty(hitEventName))
            hitEventReceiver.SendCustomEvent(hitEventName);
        player.PlayHapticEventInHand(
            isLeft ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right,
            0.1f, Mathf.Clamp01(speed / 8f), 0.5f);
        if (readout != null)
            readout.text = string.Format("{0}  {1}  {2}  {3:0.0} m/s",
                PunchClassifier.TypeName(type), gameObject.name, isLeft ? "L" : "R", speed);
    }
}
