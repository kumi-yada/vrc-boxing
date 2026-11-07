using UnityEngine;

// Ring-buffer operations for one tracked point. All state lives in BoxStation
// (UdonSharp cannot instantiate user classes, so this is a static helper).
// ring: 0 = left fist, 1 = right fist, 2 = head. age 0 = newest sample.
public static class MotionRing
{
    public static void Sample(BoxStation s, int r, Vector3 p, Vector3 a, float t, float dt)
    {
        Vector3 v = Vector3.zero;
        if (s.ringHasPrev[r] && dt > 0.0001f)
        {
            v = (p - s.ringPrev[r]) / dt;
            // clamp absurd spikes (frame hitches) so they cannot fake a punch
            if (v.sqrMagnitude > 900f) v = v.normalized * 30f;
        }
        s.ringPrev[r] = p;
        s.ringHasPrev[r] = true;
        int len = s.ringPos[r].Length;
        s.ringHead[r] = (s.ringHead[r] + 1) % len;
        if (s.ringCount[r] < len) s.ringCount[r]++;
        int h = s.ringHead[r];
        s.ringPos[r][h] = p;
        s.ringVel[r][h] = v;
        s.ringAux[r][h] = a;
        s.ringTime[r][h] = t;
    }

    private static int Idx(BoxStation s, int r, int age)
    {
        int len = s.ringPos[r].Length;
        int i = s.ringHead[r] - age;
        while (i < 0) i += len;
        return i;
    }

    public static Vector3 PosAt(BoxStation s, int r, int age) { return s.ringPos[r][Idx(s, r, age)]; }
    public static Vector3 VelAt(BoxStation s, int r, int age) { return s.ringVel[r][Idx(s, r, age)]; }
    public static Vector3 AuxAt(BoxStation s, int r, int age) { return s.ringAux[r][Idx(s, r, age)]; }
    public static float TimeAt(BoxStation s, int r, int age) { return s.ringTime[r][Idx(s, r, age)]; }

    // smallest age whose sample is not newer than t
    public static int AgeClosestTo(BoxStation s, int r, float t)
    {
        for (int age = 0; age < s.ringCount[r]; age++)
        {
            if (TimeAt(s, r, age) <= t) return age;
        }
        return s.ringCount[r] > 0 ? s.ringCount[r] - 1 : 0;
    }

    public static float PeakSpeedSince(BoxStation s, int r, float t)
    {
        float m = 0f;
        for (int age = 0; age < s.ringCount[r]; age++)
        {
            if (TimeAt(s, r, age) < t) break; // older than the window
            float sq = VelAt(s, r, age).sqrMagnitude;
            if (sq > m) m = sq;
        }
        return Mathf.Sqrt(m);
    }
}
