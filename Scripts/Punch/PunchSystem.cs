using UnityEngine;
using VRC.SDKBase;

// Orchestrates the whole punch pipeline for the local player:
//   hand/head tracking -> swept hit detection -> form validation
//   -> pad reaction + haptic feedback + debug panel.
// Static helper (UdonSharp cannot instantiate user classes): all state lives
// on BoxStation, which drives this from its Start/Update.
public static class PunchSystem
{
    public static void Initialize(BoxStation s)
    {
        const int ringCap = 128;
        s.ringPos = new Vector3[3][];
        s.ringVel = new Vector3[3][];
        s.ringAux = new Vector3[3][];
        s.ringTime = new float[3][];
        for (int i = 0; i < 3; i++)
        {
            s.ringPos[i] = new Vector3[ringCap];
            s.ringVel[i] = new Vector3[ringCap];
            s.ringAux[i] = new Vector3[ringCap];
            s.ringTime[i] = new float[ringCap];
        }
        s.ringHead = new int[3];
        s.ringCount = new int[3];
        s.ringPrev = new Vector3[3];
        s.ringHasPrev = new bool[3];

        const int maxTargets = 8;
        s.targetGo = new GameObject[maxTargets];
        s.targetCol = new Collider[maxTargets];
        s.targetType = new PunchTargetType[maxTargets];
        s.targetExpected = new PunchHand[maxTargets];
        s.targetTele = new bool[maxTargets];
        s.targetDip = new bool[maxTargets];
        s.targetLabel = new string[maxTargets];
        s.targetPad = new BoxPad[maxTargets];
        s.targetBaseScale = new Vector3[maxTargets];
        s.targetSquash = new float[maxTargets];
        for (int i = 0; i < maxTargets; i++) s.targetSquash[i] = 1f;
        s.targetCount = 0;

        const int maxLatches = 8;
        s.latchKeys = new long[maxLatches];
        s.latchTargets = new int[maxLatches];
        s.latchCount = 0;

        s.handCooldown = new float[2];

        const int maxRecords = 16;
        s.recTime = new float[maxRecords];
        s.recHand = new PunchHand[maxRecords];
        s.recType = new PunchTargetType[maxRecords];
        s.recName = new string[maxRecords];
        s.recHit = new Vector3[maxRecords];
        s.recDir = new Vector3[maxRecords];
        s.recSpeed = new float[maxRecords];
        s.recScore = new float[maxRecords];
        s.recYaw = new float[maxRecords];
        s.recGuard = new float[maxRecords];
        s.recPull = new float[maxRecords];
        s.recDip = new float[maxRecords];
        s.recNoHip = new bool[maxRecords];
        s.recGuardDown = new bool[maxRecords];
        s.recTele = new bool[maxRecords];
        s.recNoRet = new bool[maxRecords];
        s.recRetResolved = new bool[maxRecords];
        s.recQuality = new PunchQuality[maxRecords];
        s.recHead = -1;
        s.recCount = 0;

        s.pendingRecs = new int[maxRecords];
        s.pendingCount = 0;

        s.overlapBuf = new Collider[16];

        RegisterWall(s);
        RegisterGamePad(s, s.leftPad, true);
        RegisterGamePad(s, s.rightPad, false);

        PunchDebugPanel.Initialize(s);
        PunchDebugPanel.SetVisible(s, s.debugPanelVisible);
    }

    // ---- test wall: pre-placed "Wall" child of the station, one pad per real
    // punch target, in station-local space ----
    // The player stands on the -x side of the station facing +x, so the
    // player's right side is -z. Layout: jab ahead, crosses on the opposite
    // sides, hooks at temple height, uppercut low in the center.
    private static void RegisterWall(BoxStation s)
    {
        RegisterWallPad(s, "Jab", PunchTargetType.Jab, PunchHand.Any, true, true);
        RegisterWallPad(s, "CrossL", PunchTargetType.CrossLeft, PunchHand.Right, true, true);
        RegisterWallPad(s, "CrossR", PunchTargetType.CrossRight, PunchHand.Left, true, true);
        RegisterWallPad(s, "HookL", PunchTargetType.HookLeft, PunchHand.Right, true, true);
        RegisterWallPad(s, "HookR", PunchTargetType.HookRight, PunchHand.Left, true, true);
        // an uppercut dips by design, so the telegraph check is disabled for it
        // (documented in TUNING.md)
        RegisterWallPad(s, "UpperCut", PunchTargetType.UpperCut, PunchHand.Any, false, false);
    }

    private static void RegisterWallPad(BoxStation s, string name, PunchTargetType type,
        PunchHand expected, bool tele, bool dip)
    {
        Transform t = s.transform.Find("Wall/" + name);
        if (t == null) return;
        Collider col = t.GetComponent<Collider>();
        if (col == null) return;
        RegisterTarget(s, t.gameObject, col, type, expected, tele, dip, name, null);
    }

    private static void RegisterGamePad(BoxStation s, BoxPad pad, bool isLeft)
    {
        if (pad == null) return;
        Collider col = pad.GetPadCollider();
        if (col == null) return;
        RegisterTarget(s, pad.gameObject, col, PunchTargetType.GamePad,
            isLeft ? PunchHand.Left : PunchHand.Right, true, true,
            isLeft ? "PadL" : "PadR", pad);
    }

    private static void RegisterTarget(BoxStation s, GameObject go, Collider col,
        PunchTargetType type, PunchHand expected, bool tele, bool dip, string label, BoxPad pad)
    {
        int i = s.targetCount;
        if (i >= s.targetGo.Length) return;
        s.targetGo[i] = go;
        s.targetCol[i] = col;
        s.targetType[i] = type;
        s.targetExpected[i] = expected;
        s.targetTele[i] = tele;
        s.targetDip[i] = dip;
        s.targetLabel[i] = label;
        s.targetPad[i] = pad;
        s.targetBaseScale[i] = go.transform.localScale;
        s.targetSquash[i] = 1f;
        s.targetCount++;
    }

    // linear scan over the (few) registered targets; Dictionary/List are not
    // available in UdonSharp
    private static int FindTargetIndex(BoxStation s, int colliderInstanceID)
    {
        for (int i = 0; i < s.targetCount; i++)
        {
            if (s.targetCol[i] != null && s.targetCol[i].GetInstanceID() == colliderInstanceID)
                return i;
        }
        return -1;
    }

    // ---- per-frame update for the local player ----
    public static void UpdateLocal(BoxStation s, VRCPlayerApi player)
    {
        float now = Time.time;
        float dt = Time.deltaTime;
        if (dt <= 0f) dt = 0.016f;

        Vector3 headPos = player.GetBonePosition(HumanBodyBones.Head);
        Quaternion headRot = player.GetBoneRotation(HumanBodyBones.Head);
        Vector3 lp = player.GetBonePosition(HumanBodyBones.LeftHand);
        Vector3 rp = player.GetBonePosition(HumanBodyBones.RightHand);

        MotionRing.Sample(s, 2, headPos, headRot * Vector3.forward, now, dt);
        MotionRing.Sample(s, 0, lp, Vector3.zero, now, dt);
        MotionRing.Sample(s, 1, rp, Vector3.zero, now, dt);

        s.fbPlayer = player;

        SweepHand(s, PunchHand.Left, 0, 1, now);
        SweepHand(s, PunchHand.Right, 1, 0, now);
        UpdateRetractions(s, now);

        for (int i = 0; i < s.targetCount; i++) UpdateTargetAnim(s, i, dt);
    }

    // Swept-sphere hit detection: the fist travels up to ~15 cm per frame at
    // full swing, so the prev->cur segment is substepped at half the fist
    // radius to guarantee no tunneling. One swing counts once (latch + cooldown).
    private static void SweepHand(BoxStation s, PunchHand hand, int ring, int otherRing, float now)
    {
        UpdateLatches(s, hand, s.ringCount[ring] > 0 ? MotionRing.PosAt(s, ring, 0) : Vector3.zero);

        if (s.ringCount[ring] < 2) return;
        if (now < s.handCooldown[(int)hand]) return;

        Vector3 prev = MotionRing.PosAt(s, ring, 1);
        Vector3 cur = MotionRing.PosAt(s, ring, 0);
        Vector3 delta = cur - prev;
        float travel = delta.magnitude;
        if (travel < 0.0005f) return;

        float speedNow = MotionRing.VelAt(s, ring, 0).magnitude;
        if (speedNow < s.minPunchSpeed) return; // light touch: ignore

        Vector3 dir = delta / travel;
        int steps = Mathf.Clamp(Mathf.CeilToInt(travel / (s.fistRadius * 0.5f)), 1, 12);

        for (int st = 1; st <= steps; st++)
        {
            Vector3 p = prev + dir * (travel * st / steps);
            int n = Physics.OverlapSphereNonAlloc(p, s.fistRadius, s.overlapBuf,
                -1, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider c = s.overlapBuf[i];
                if (c == null) continue;
                int ti = FindTargetIndex(s, c.GetInstanceID());
                if (ti < 0) continue;
                if (!TargetActive(s, ti)) continue;

                long key = ((long)(int)hand << 32) | (long)(uint)c.GetInstanceID();
                if (LatchContains(s, key)) continue; // this swing already counted

                float speed = Mathf.Max(MotionRing.PeakSpeedSince(s, ring, now - 0.08f), speedNow);
                int rec = PunchFormValidator.Evaluate(s, ring, otherRing, ti, hand, p, dir, speed, now);

                LatchAdd(s, key, ti);
                s.handCooldown[(int)hand] = now + s.hitCooldown;
                Dispatch(s, rec, ti, hand);
                return;
            }
        }
    }

    private static void UpdateLatches(BoxStation s, PunchHand hand, Vector3 fistPos)
    {
        for (int i = s.latchCount - 1; i >= 0; i--)
        {
            if ((int)(s.latchKeys[i] >> 32) != (int)hand) continue;
            int n = Physics.OverlapSphereNonAlloc(fistPos, s.fistRadius + 0.03f,
                s.overlapBuf, -1, QueryTriggerInteraction.Collide);
            bool still = false;
            for (int k = 0; k < n; k++)
            {
                if (s.overlapBuf[k] == s.targetCol[s.latchTargets[i]]) { still = true; break; }
            }
            if (!still) LatchRemoveAt(s, i);
        }
    }

    private static bool LatchContains(BoxStation s, long key)
    {
        for (int i = 0; i < s.latchCount; i++)
            if (s.latchKeys[i] == key) return true;
        return false;
    }

    private static void LatchAdd(BoxStation s, long key, int target)
    {
        if (s.latchCount >= s.latchKeys.Length) return;
        s.latchKeys[s.latchCount] = key;
        s.latchTargets[s.latchCount] = target;
        s.latchCount++;
    }

    private static void LatchRemoveAt(BoxStation s, int idx)
    {
        for (int i = idx; i < s.latchCount - 1; i++)
        {
            s.latchKeys[i] = s.latchKeys[i + 1];
            s.latchTargets[i] = s.latchTargets[i + 1];
        }
        s.latchCount--;
    }

    private static bool TargetActive(BoxStation s, int i)
    {
        return s.targetGo[i] != null && s.targetGo[i].activeSelf &&
               s.targetCol[i] != null && s.targetCol[i].enabled;
    }

    private static void Dispatch(BoxStation s, int rec, int target, PunchHand hand)
    {
        s.targetSquash[target] = 0f;
        BoxPad pad = s.targetPad[target];
        if (pad != null)
            pad.NotifyPunchHit(hand == PunchHand.Left, s.recSpeed[rec], s.recScore[rec], s.recQuality[rec]);
        PunchFeedback.Show(s, rec, hand);
        PunchDebugPanel.Refresh(s);
    }

    private static void UpdateRetractions(BoxStation s, float now)
    {
        for (int i = s.pendingCount - 1; i >= 0; i--)
        {
            int rec = s.pendingRecs[i];
            int ring = s.recHand[rec] == PunchHand.Left ? 0 : 1;
            int status = PunchFormValidator.CheckRetraction(s, ring, rec, now);
            if (status == 0) continue;
            s.recRetResolved[rec] = true;
            if (status == 2)
            {
                s.recNoRet[rec] = true;
                PunchFormValidator.ComputeQuality(s, rec);
            }
            PunchDebugPanel.Refresh(s);
            // remove from pending
            for (int k = i; k < s.pendingCount - 1; k++)
                s.pendingRecs[k] = s.pendingRecs[k + 1];
            s.pendingCount--;
        }
    }

    private static void UpdateTargetAnim(BoxStation s, int i, float dt)
    {
        if (s.targetSquash[i] >= 1f) return;
        s.targetSquash[i] += dt * 5f;
        if (s.targetSquash[i] > 1f) s.targetSquash[i] = 1f;
        float k = Mathf.Sin(s.targetSquash[i] * Mathf.PI); // 0 -> 1 -> 0
        Vector3 b = s.targetBaseScale[i];
        s.targetGo[i].transform.localScale = new Vector3(
            b.x * (1f + 0.9f * k),
            b.y * (1f - 0.35f * k),
            b.z * (1f - 0.35f * k));
    }
}
