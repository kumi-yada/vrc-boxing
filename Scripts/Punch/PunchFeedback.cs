using UnityEngine;
using VRC.SDKBase;

// Immediate per-punch feedback: controller haptics scaled by impact.
// (Floating labels and impact sound are intentionally not used for now.)
// Static helper; the player reference lives on BoxStation.
public static class PunchFeedback
{
    public static void Show(BoxStation s, int rec, PunchHand hand)
    {
        if (s.fbPlayer == null) return;

        float amp = Mathf.Clamp01(s.recSpeed[rec] / s.maxSpeedForFeedback);
        if (s.recQuality[rec] == PunchQuality.Invalid) amp *= 0.35f;
        VRC_Pickup.PickupHand h = hand == PunchHand.Left
            ? VRC_Pickup.PickupHand.Left
            : VRC_Pickup.PickupHand.Right;
        s.fbPlayer.PlayHapticEventInHand(h, 0.1f, amp, 0.5f);
    }
}
