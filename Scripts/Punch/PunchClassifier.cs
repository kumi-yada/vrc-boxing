using UnityEngine;

// Classifies a punch from the fist's travel direction over the final stretch of the swing:
//   mostly upward              -> Uppercut
//   horizontal, toward target  -> Jab
//   horizontal, from the side  -> Hook
public static class PunchClassifier
{
    public const float UppercutMinUp = 0.5f; // upward share of travel that counts as an uppercut
    public const float JabMinForward = 0.6f; // alignment with the head->target line that counts as a jab

    public static PunchType Classify(Vector3 travel, Vector3 headPos, Vector3 targetPos)
    {
        float len = travel.magnitude;
        if (len < 0.03f) return PunchType.None;

        Vector3 dir = travel / len;
        if (dir.y >= UppercutMinUp) return PunchType.Uppercut;

        Vector3 toTarget = targetPos - headPos;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return PunchType.Jab;

        Vector3 fwd = dir;
        fwd.y = 0f;
        fwd = fwd.normalized;
        if (Vector3.Dot(fwd, toTarget.normalized) >= JabMinForward) return PunchType.Jab;
        return PunchType.Hook;
    }

    public static string TypeName(PunchType type)
    {
        switch (type)
        {
            case PunchType.Jab: return "JAB";
            case PunchType.Hook: return "HOOK";
            case PunchType.Uppercut: return "UPPERCUT";
            default: return "HIT";
        }
    }
}
