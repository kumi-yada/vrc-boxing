using UnityEngine;

// Judges the form of one punch from the motion history around impact.
// Static because UdonSharp cannot instantiate user classes; all state and
// thresholds live on BoxStation. Creates a record (parallel arrays) and
// returns its index.
public static class PunchFormValidator
{
    public static int NewRecord(BoxStation s)
    {
        s.recHead = (s.recHead + 1) % s.recTime.Length;
        if (s.recCount < s.recTime.Length) s.recCount++;
        return s.recHead;
    }

    public static int Evaluate(BoxStation s, int ring, int otherRing, int target, PunchHand hand,
        Vector3 impactPos, Vector3 punchDir, float speed, float now)
    {
        int rec = NewRecord(s);
        s.recTime[rec] = now;
        s.recHand[rec] = hand;
        s.recType[rec] = s.targetType[target];
        s.recName[rec] = s.targetLabel[target];
        s.recHit[rec] = impactPos;
        s.recDir[rec] = punchDir.normalized;
        s.recSpeed[rec] = speed;
        Vector3 axis = s.recDir[rec];

        // ---- hip drive: signed head yaw over the drive window ----
        int a0 = MotionRing.AgeClosestTo(s, 2, now - s.hipWindow);
        Vector3 f0 = MotionRing.AuxAt(s, 2, a0);
        Vector3 f1 = MotionRing.AuxAt(s, 2, 0);
        Vector3 f0h = Vector3.ProjectOnPlane(f0, Vector3.up);
        Vector3 f1h = Vector3.ProjectOnPlane(f1, Vector3.up);
        if (f0h.sqrMagnitude < 0.001f) f0h = f0;
        if (f1h.sqrMagnitude < 0.001f) f1h = f1;
        f0h = f0h.normalized;
        f1h = f1h.normalized;
        float yawDelta = Vector3.SignedAngle(f0h, f1h, Vector3.up);
        s.recYaw[rec] = yawDelta;

        Vector3 punchH = Vector3.ProjectOnPlane(axis, Vector3.up);
        bool hipOk;
        if (punchH.sqrMagnitude < 0.09f)
        {
            // straight punch: any real torso rotation counts
            hipOk = Mathf.Abs(yawDelta) >= s.hipDriveMinDeg;
        }
        else
        {
            punchH = punchH.normalized;
            float expected = Vector3.Dot(Vector3.Cross(f0h, punchH), Vector3.up);
            if (s.hipRequireDirection && Mathf.Abs(expected) > 0.2f)
                hipOk = yawDelta * Mathf.Sign(expected) >= s.hipDriveMinDeg;
            else
                hipOk = Mathf.Abs(yawDelta) >= s.hipDriveMinDeg;
        }
        s.recNoHip[rec] = !hipOk;

        // ---- guard: other hand near the chin at impact ----
        Vector3 headPos = MotionRing.PosAt(s, 2, 0);
        Vector3 headFwd = MotionRing.AuxAt(s, 2, 0);
        Vector3 chin = headPos + Vector3.down * 0.15f + headFwd * 0.05f;
        int gOldest = MotionRing.AgeClosestTo(s, otherRing, now - s.guardWindow);
        int limit = Mathf.Min(gOldest, s.ringCount[otherRing] - 1);
        float gMin = float.MaxValue;
        for (int age = 0; age <= limit; age++)
        {
            float d = (MotionRing.PosAt(s, otherRing, age) - chin).magnitude;
            if (d < gMin) gMin = d;
        }
        if (gMin == float.MaxValue) gMin = (MotionRing.PosAt(s, otherRing, 0) - chin).magnitude;
        s.recGuard[rec] = gMin;
        s.recGuardDown[rec] = gMin > s.guardMaxDist;

        // ---- telegraph: hand pulled back / dipped during the approach ----
        bool dipCounts = s.targetDip[target];
        if (s.targetType[target] == PunchTargetType.UpperCut) dipCounts = false;
        BoxPad pad = s.targetPad[target];
        if (pad != null && pad.GetPadPosition() == PadPosition.UpperCut) dipCounts = false;

        s.recTele[rec] = false;
        s.recPull[rec] = 0f;
        s.recDip[rec] = 0f;
        if (s.targetTele[target])
        {
            float winStart = now - s.telegraphWindow;
            float winEnd = now - s.telegraphExcludeFinal;
            // walk oldest -> newest inside the window (age decreases => time
            // increases); a drop from the running max larger than the threshold
            // means the hand moved away mid-approach
            float runMaxA = float.MinValue;
            float runMaxY = float.MinValue;
            int count = s.ringCount[ring];
            for (int age = count - 1; age >= 0; age--)
            {
                float ta = MotionRing.TimeAt(s, ring, age);
                if (ta < winStart) continue; // older than the window
                if (ta > winEnd) break;      // reached the final acceleration
                Vector3 hp = MotionRing.PosAt(s, ring, age);
                float a = Vector3.Dot(hp - impactPos, axis);
                if (a > runMaxA) runMaxA = a;
                else if (runMaxA - a > s.recPull[rec]) s.recPull[rec] = runMaxA - a;
                if (dipCounts)
                {
                    if (hp.y > runMaxY) runMaxY = hp.y;
                    else if (runMaxY - hp.y > s.recDip[rec]) s.recDip[rec] = runMaxY - hp.y;
                }
            }
            s.recTele[rec] = s.recPull[rec] >= s.telegraphMinDist ||
                             (dipCounts && s.recDip[rec] >= s.telegraphDip);
        }

        s.recNoRet[rec] = false;
        s.recRetResolved[rec] = false;
        ComputeQuality(s, rec);
        s.pendingRecs[s.pendingCount] = rec;
        s.pendingCount++;
        return rec;
    }

    // 0 = still pending, 1 = retracted in time, 2 = failed to retract
    public static int CheckRetraction(BoxStation s, int ring, int rec, float now)
    {
        if (HasRetracted(s, ring, rec)) return 1;
        if (now - s.recTime[rec] > s.retractWindow) return 2;
        return 0;
    }

    private static bool HasRetracted(BoxStation s, int ring, int rec)
    {
        int hAge = MotionRing.AgeClosestTo(s, 2, s.recTime[rec]);
        Vector3 chin = MotionRing.PosAt(s, 2, hAge) + Vector3.down * 0.15f + MotionRing.AuxAt(s, 2, hAge) * 0.05f;
        int count = s.ringCount[ring];
        for (int age = 0; age < count; age++)
        {
            if (MotionRing.TimeAt(s, ring, age) < s.recTime[rec]) break; // older than impact
            if ((MotionRing.PosAt(s, ring, age) - chin).magnitude <= s.retractMaxDist) return true;
            float a = Vector3.Dot(MotionRing.PosAt(s, ring, age) - s.recHit[rec], s.recDir[rec]);
            if (a <= -s.retractMinBack) return true;
        }
        return false;
    }

    public static void ComputeQuality(BoxStation s, int rec)
    {
        if (s.recTele[rec])
        {
            s.recQuality[rec] = PunchQuality.Invalid;
            s.recScore[rec] = 0f;
            return;
        }
        int faults = 0;
        if (s.recNoHip[rec]) faults++;
        if (s.recGuardDown[rec]) faults++;
        if (s.recNoRet[rec]) faults++;
        s.recQuality[rec] = faults == 0 ? PunchQuality.Clean : PunchQuality.Sloppy;
        s.recScore[rec] = s.recSpeed[rec] * (faults == 0 ? 1f : s.sloppyScoreMult);
    }
}
