using UdonSharp;
using UnityEngine;

// Audio hook point for the Combo Cutter Station. Every station state has its
// own cue id so it can sound distinct before anything flashes; until the
// world's shared cue set lands, each id maps to a placeholder clip + pitch.
// The shared cue set can either replace the clips or listen on cueReceiver
// (event name = cueEventPrefix + CueName(id), LastCue holds the id).
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ComboCuePlayer : UdonSharpBehaviour
{
    public const int CUE_CALIBRATE_START = 0;
    public const int CUE_CALIBRATE_RETRY = 1;
    public const int CUE_CALIBRATE_DONE = 2;
    public const int CUE_ANNOUNCE = 3;
    public const int CUE_STEP_LIT = 4;
    public const int CUE_PERFECT = 5;
    public const int CUE_GOOD = 6;
    public const int CUE_MISS = 7;
    public const int CUE_COMBO_LANDED = 8;
    public const int CUE_GUARD_READY = 9;
    public const int CUE_RUN_COMPLETE = 10;
    public const int CUE_RESTART = 11;
    public const int CUE_VR_ONLY = 12;
    public const int CUE_COUNT = 13;

    public AudioSource source;

    [Header("Per-cue clips (index = cue id, see CueName)")]
    public AudioClip[] clips = new AudioClip[CUE_COUNT];
    public float[] pitches = new float[CUE_COUNT];
    public float[] volumes = new float[CUE_COUNT];

    [Header("Shared cue set hook")]
    public UdonSharpBehaviour cueReceiver;
    public string cueEventPrefix = "OnComboCue";
    [HideInInspector] public int LastCue = -1;

    public void Play(int cue)
    {
        if (cue < 0 || cue >= CUE_COUNT) return;
        LastCue = cue;
        if (source != null && clips != null && cue < clips.Length && clips[cue] != null)
        {
            source.pitch = PitchFor(cue);
            source.PlayOneShot(clips[cue], VolumeFor(cue));
        }
        if (cueReceiver != null && !string.IsNullOrEmpty(cueEventPrefix))
            cueReceiver.SendCustomEvent(cueEventPrefix + CueName(cue));
    }

    private float PitchFor(int cue)
    {
        if (pitches == null || cue >= pitches.Length || pitches[cue] <= 0f) return 1f;
        return pitches[cue];
    }

    private float VolumeFor(int cue)
    {
        if (volumes == null || cue >= volumes.Length || volumes[cue] <= 0f) return 1f;
        return volumes[cue];
    }

    public string CueName(int cue)
    {
        switch (cue)
        {
            case CUE_CALIBRATE_START: return "CalibrateStart";
            case CUE_CALIBRATE_RETRY: return "CalibrateRetry";
            case CUE_CALIBRATE_DONE: return "CalibrateDone";
            case CUE_ANNOUNCE: return "Announce";
            case CUE_STEP_LIT: return "StepLit";
            case CUE_PERFECT: return "Perfect";
            case CUE_GOOD: return "Good";
            case CUE_MISS: return "Miss";
            case CUE_COMBO_LANDED: return "ComboLanded";
            case CUE_GUARD_READY: return "GuardReady";
            case CUE_RUN_COMPLETE: return "RunComplete";
            case CUE_RESTART: return "Restart";
            case CUE_VR_ONLY: return "VrOnly";
            default: return "Unknown";
        }
    }
}
