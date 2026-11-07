using UnityEngine;

// Text/color helpers for a punch record stored in BoxStation's record arrays.
// (UdonSharp cannot instantiate user classes, so records are parallel arrays
// indexed by record id and these helpers are static.)
public static class PunchRecordUtil
{
    public static string FaultsText(BoxStation s, int rec)
    {
        if (s.recTele[rec]) return "Telegraphed!";
        string str = "";
        if (s.recNoHip[rec]) str += "no hip drive ";
        if (s.recGuardDown[rec]) str += "guard down ";
        if (!s.recRetResolved[rec]) str += "retract? ";
        else if (s.recNoRet[rec]) str += "no retraction ";
        if (str.Length > 0) str = str.Substring(0, str.Length - 1);
        return str;
    }

    public static string QualityText(BoxStation s, int rec)
    {
        if (s.recQuality[rec] == PunchQuality.Clean) return "Clean!";
        if (s.recQuality[rec] == PunchQuality.Sloppy) return "Sloppy";
        return "Invalid";
    }

    public static Color QualityColor(BoxStation s, int rec)
    {
        if (s.recQuality[rec] == PunchQuality.Clean) return new Color(0.4f, 1f, 0.4f);
        if (s.recQuality[rec] == PunchQuality.Sloppy) return new Color(1f, 0.8f, 0.27f);
        return new Color(1f, 0.33f, 0.33f);
    }
}
