using UnityEngine;
using TMPro;

// World-space debug/tuning panel: the last several punches with their measured
// values (speed, head rotation, guard distance, pullback, flags, quality,
// score) plus the active thresholds. Toggle with P (desktop) or
// BoxStation.ToggleDebugPanel(). Static helper; UI state lives on BoxStation.
public static class PunchDebugPanel
{
    public static void Initialize(BoxStation s)
    {
        // The panel is pre-placed in the scene as an inactive child of the
        // station ("PunchDebugPanel": world-space canvas + Text); just wire it up.
        Transform root = s.transform.Find("PunchDebugPanel");
        if (root == null) return;
        s.panelRoot = root.gameObject;

        Transform textT = root.Find("Text");
        if (textT == null) return;
        s.panelText = textT.GetComponent<TextMeshProUGUI>();
        if (s.panelText == null) return;

        Refresh(s);
    }

    public static bool Visible(BoxStation s)
    {
        return s.panelRoot != null && s.panelRoot.activeSelf;
    }

    public static void SetVisible(BoxStation s, bool v)
    {
        if (s.panelRoot != null) s.panelRoot.SetActive(v);
    }

    public static void Toggle(BoxStation s)
    {
        SetVisible(s, !Visible(s));
    }

    public static void Refresh(BoxStation s)
    {
        if (s.panelText == null || s.recTime == null) return;
        string str = string.Format(
            "PUNCH DEBUG  [minV {0:0.0} m/s | hip {1:0} deg | guard {2:0.00} m | tel {3:0.00} m | ret {4:0.00} s]  P = toggle\n",
            s.minPunchSpeed, s.hipDriveMinDeg, s.guardMaxDist, s.telegraphMinDist, s.retractWindow);
        int lines = Mathf.Min(s.debugPanelLines, s.recCount);
        for (int i = 0; i < lines; i++)
        {
            int rec = (s.recHead - i + s.recTime.Length) % s.recTime.Length;
            string q;
            if (s.recQuality[rec] == PunchQuality.Clean) q = "<color=#66FF66>CLEAN</color>";
            else if (s.recQuality[rec] == PunchQuality.Sloppy) q = "<color=#FFCC44>SLOPPY</color>";
            else q = "<color=#FF5555>INVALID</color>";
            string hand = s.recHand[rec] == PunchHand.Left ? "L" : "R";
            string yaw = (s.recYaw[rec] >= 0f ? "+" : "") + string.Format("{0:0}", s.recYaw[rec]);
            str += string.Format("{0} {1,-7} v={2:0.0} yaw={3} g={4:0.00} pb={5:0.00} {6} {7} s={8:0.0}\n",
                hand, s.recName[rec], s.recSpeed[rec], yaw, s.recGuard[rec], s.recPull[rec],
                PunchRecordUtil.FaultsText(s, rec), q, s.recScore[rec]);
        }
        s.panelText.text = str;
    }
}
