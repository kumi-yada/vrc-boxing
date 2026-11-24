using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Single punch-detection loop for the whole world — add this component once to
// the scene. Every PunchTarget registers itself here in Start. One Update
// tracks the local player's two hand bones once (ring-buffer history, then a
// swept sphere along the prev->cur fist segment substepped at half the fist
// radius so no tunneling) and tests every registered target against that one
// swing. The last `classifyDistance` meters of fist travel are classified as
// jab/hook/uppercut once per hand per frame and shared by all targets hit in
// that frame. Per-target latch/cooldown stay on the PunchTarget.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PunchDetector : UdonSharpBehaviour
{
    [Header("Detection")]
    public float fistRadius = 0.10f;   // m, swept-sphere radius of a fist
    public float minPunchSpeed = 0.5f; // m/s; below this a touch is not a punch
    public float hitCooldown = 0.25f;  // s, per target per hand: one swing = one hit per target

    [Header("Classification")]
    public float classifyDistance = 0.10f; // m of fist travel at the end of the swing used to classify
    public int historySize = 12;           // ring-buffer samples kept per hand

    private PunchTarget[] targets = new PunchTarget[16];
    private int targetCount = 0;

    // per-hand state; index 0 = left, 1 = right
    private Vector3[] prevPos = new Vector3[2];
    private bool[] hasPrev = new bool[2];
    private Vector3[][] histPos;
    private float[][] histTime;
    private int[] histHead = new int[2];
    private int[] histCount = new int[2];
    private Collider[] overlapBuf = new Collider[8];
    private bool historyReady;

    void Start()
    {
        InitHistory();
    }

    public void Register(PunchTarget target)
    {
        for (int i = 0; i < targetCount; i++)
            if (targets[i] == target) return;
        if (targetCount >= targets.Length)
        {
            PunchTarget[] grown = new PunchTarget[targets.Length * 2];
            for (int i = 0; i < targetCount; i++) grown[i] = targets[i];
            targets = grown;
        }
        targets[targetCount++] = target;
    }

    public void Unregister(PunchTarget target)
    {
        for (int i = 0; i < targetCount; i++)
        {
            if (targets[i] != target) continue;
            for (int j = i; j < targetCount - 1; j++) targets[j] = targets[j + 1];
            targets[--targetCount] = null;
            return;
        }
    }

    void Update()
    {
        if (!historyReady) InitHistory();
        if (targetCount == 0) return;

        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null) return;
        CheckHand(player, 0, player.GetBonePosition(HumanBodyBones.LeftHand));
        CheckHand(player, 1, player.GetBonePosition(HumanBodyBones.RightHand));
    }

    private void InitHistory()
    {
        if (historySize < 4) historySize = 4;
        histPos = new Vector3[2][];
        histTime = new float[2][];
        for (int h = 0; h < 2; h++)
        {
            histPos[h] = new Vector3[historySize];
            histTime[h] = new float[historySize];
        }
        historyReady = true;
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

        if (travel < 0.0005f) return;
        float speedNow = travel / dt;
        if (speedNow < minPunchSpeed) return;

        // classification window shared by every target hit this frame
        float segStart;
        Vector3 travelWin = fist - SampleBackByDistance(h, classifyDistance, out segStart);
        float segTime = now - segStart;
        if (travelWin.magnitude < 0.03f)
        {
            travelWin = delta;
            segTime = dt;
        }
        float speed = Mathf.Max(speedNow, travelWin.magnitude / Mathf.Max(segTime, 0.001f));
        Vector3 head = player.GetBonePosition(HumanBodyBones.Head);

        Vector3 dir = delta / travel;
        int steps = Mathf.Clamp(Mathf.CeilToInt(travel / (fistRadius * 0.5f)), 1, 12);

        for (int t = 0; t < targetCount; t++)
        {
            PunchTarget target = targets[t];
            if (target == null || !target.enabled || !target.gameObject.activeInHierarchy) continue;
            Collider col = target.GetHitCollider();
            if (col == null || !col.enabled) continue;

            // one swing = one hit on this target: stay latched until the fist leaves it
            if (target.latched[h])
            {
                if (OverlapsAt(col, fist)) continue;
                target.latched[h] = false;
            }
            if (now < target.cooldownUntil[h]) continue;

            for (int s = 1; s <= steps; s++)
            {
                Vector3 p = prev + dir * (travel * s / steps);
                if (!OverlapsAt(col, p)) continue;

                PunchType type = PunchClassifier.Classify(travelWin, head, p);
                target.latched[h] = true;
                target.cooldownUntil[h] = now + hitCooldown;
                target.NotifyPunchHit(player, h == 0, type, speed);
                break;
            }
        }
    }

    private bool OverlapsAt(Collider col, Vector3 p)
    {
        int n = Physics.OverlapSphereNonAlloc(p, fistRadius, overlapBuf, -1, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
            if (overlapBuf[i] == col) return true;
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

    // walks the ring buffer back along the fist path until `dist` meters of travel
    // are covered; falls back to the oldest sample if the buffer is shorter
    private Vector3 SampleBackByDistance(int h, float dist, out float t)
    {
        int count = histCount[h];
        int len = histPos[h].Length;
        int i = histHead[h];
        Vector3 pos = histPos[h][i];
        t = histTime[h][i];
        float acc = 0f;
        for (int age = 1; age < count; age++)
        {
            int j = i - age;
            while (j < 0) j += len;
            acc += Vector3.Distance(histPos[h][j], pos);
            pos = histPos[h][j];
            t = histTime[h][j];
            if (acc >= dist) break;
        }
        return pos;
    }
}
