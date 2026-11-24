
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Attach to any object the player can punch. On each client it watches the
// local player's two hand bones: a swept sphere along the prev->cur fist
// segment detects the hit (substepped at half the fist radius, so no
// tunneling), the fist travel over the last `classifyWindow` seconds is
// classified as jab/hook/uppercut, and the hit registers: squash animation +
// haptic pulse on the puncher, plus the last punch written to `readout`.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PunchTarget : UdonSharpBehaviour
{
    public string label = "";   // readout name; empty falls back to the GameObject name
    public TMP_Text readout;    // optional; last punch is written here

    [Header("Detection")]
    public float fistRadius = 0.10f;   // m, swept-sphere radius of a fist
    public float minPunchSpeed = 0.5f; // m/s; below this a touch is not a punch
    public float hitCooldown = 0.25f;  // s, per hand: one swing = one hit

    [Header("Classification")]
    public float classifyWindow = 0.15f; // s of fist travel used to classify the punch
    public int historySize = 12;         // ring-buffer samples kept per hand

    private Collider hitCollider;
    private Vector3 baseScale;
    private float squash = 1f;

    // per-hand state; index 0 = left, 1 = right
    private Vector3[] prevPos = new Vector3[2];
    private bool[] hasPrev = new bool[2];
    private float[] cooldownUntil = new float[2];
    private bool[] latched = new bool[2];
    private Vector3[][] histPos;
    private float[][] histTime;
    private int[] histHead = new int[2];
    private int[] histCount = new int[2];
    private Collider[] overlapBuf = new Collider[8];

    void Start()
    {
        hitCollider = GetComponent<Collider>();
        if (string.IsNullOrEmpty(label)) label = gameObject.name;
        baseScale = transform.localScale;
        if (historySize < 4) historySize = 4;
        histPos = new Vector3[2][];
        histTime = new float[2][];
        for (int h = 0; h < 2; h++)
        {
            histPos[h] = new Vector3[historySize];
            histTime[h] = new float[historySize];
        }
    }

    void Update()
    {
        AnimateSquash();
        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null || hitCollider == null || !hitCollider.enabled) return;
        CheckHand(player, 0, player.GetBonePosition(HumanBodyBones.LeftHand));
        CheckHand(player, 1, player.GetBonePosition(HumanBodyBones.RightHand));
    }

    private void CheckHand(VRCPlayerApi player, int h, Vector3 fist)
    {
        float now = Time.time;
        float dt = Time.deltaTime;
        if (dt <= 0f) dt = 0.016f;

        PushHistory(h, fist, now);

        if (!hasPrev[h])
        {
            prevPos[h] = fist;
            hasPrev[h] = true;
            return;
        }

        Vector3 prev = prevPos[h];
        Vector3 delta = fist - prev;
        float travel = delta.magnitude;
        prevPos[h] = fist;

        // one swing = one hit: stay latched until the fist leaves this target
        if (latched[h])
        {
            if (OverlapsAt(fist)) return;
            latched[h] = false;
        }
        if (now < cooldownUntil[h]) return;
        if (travel < 0.0005f) return;
        float speedNow = travel / dt;
        if (speedNow < minPunchSpeed) return;

        Vector3 dir = delta / travel;
        int steps = Mathf.Clamp(Mathf.CeilToInt(travel / (fistRadius * 0.5f)), 1, 12);
        for (int s = 1; s <= steps; s++)
        {
            Vector3 p = prev + dir * (travel * s / steps);
            if (!OverlapsAt(p)) continue;

            Vector3 travelWin = fist - SampleAtOrBefore(h, now - classifyWindow);
            if (travelWin.magnitude < 0.03f) travelWin = delta;
            float speed = Mathf.Max(speedNow, travelWin.magnitude / classifyWindow);
            PunchType type = PunchClassifier.Classify(travelWin,
                player.GetBonePosition(HumanBodyBones.Head), p);

            latched[h] = true;
            cooldownUntil[h] = now + hitCooldown;
            OnPunchHit(player, h == 0, type, speed);
            return;
        }
    }

    private void OnPunchHit(VRCPlayerApi player, bool isLeft, PunchType type, float speed)
    {
        squash = 0f;
        player.PlayHapticEventInHand(
            isLeft ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right,
            0.1f, Mathf.Clamp01(speed / 8f), 0.5f);
        if (readout != null)
            readout.text = string.Format("{0}  {1}  {2}  {3:0.0} m/s",
                PunchClassifier.TypeName(type), label, isLeft ? "L" : "R", speed);
    }

    private bool OverlapsAt(Vector3 p)
    {
        int n = Physics.OverlapSphereNonAlloc(p, fistRadius, overlapBuf, -1, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
            if (overlapBuf[i] == hitCollider) return true;
        return false;
    }

    private void PushHistory(int h, Vector3 p, float t)
    {
        int len = histPos[h].Length;
        histHead[h] = (histHead[h] + 1) % len;
        if (histCount[h] < len) histCount[h]++;
        histPos[h][histHead[h]] = p;
        histTime[h][histHead[h]] = t;
    }

    // newest sample that is not newer than t; oldest sample if t predates all of them
    private Vector3 SampleAtOrBefore(int h, float t)
    {
        int count = histCount[h];
        int len = histPos[h].Length;
        for (int age = 0; age < count; age++)
        {
            int i = histHead[h] - age;
            while (i < 0) i += len;
            if (histTime[h][i] <= t) return histPos[h][i];
        }
        int oi = histHead[h] - (count - 1);
        while (oi < 0) oi += len;
        return histPos[h][oi];
    }

    private void AnimateSquash()
    {
        if (squash >= 1f) return;
        squash = Mathf.Min(1f, squash + Time.deltaTime * 5f);
        float k = Mathf.Sin(squash * Mathf.PI); // 0 -> 1 -> 0
        transform.localScale = new Vector3(
            baseScale.x * (1f + 0.9f * k),
            baseScale.y * (1f - 0.35f * k),
            baseScale.z * (1f - 0.35f * k));
    }
}
