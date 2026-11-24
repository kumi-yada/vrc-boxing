using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Reaction Station (Punch-a-Lite): 9 pads on an arc light up one at a time and
// stay lit until punched. A 60 s run scores each hit by reaction time and ends
// with score / hits / avg + fastest reaction / letter grade. Fully local to
// each player (no [UdonSynced] fields, no network traffic), exactly how
// PunchTarget already works; hits arrive via the PunchTarget hit hook routed
// through ReactionStationPad.OnPadHit.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ReactionStationManager : UdonSharpBehaviour
{
    private const int STATE_IDLE = 0;
    private const int STATE_READY = 1;
    private const int STATE_RUNNING = 2;

    [Header("Wiring")]
    public ReactionStationPad[] pads;       // the 9 arc pads, in arc order
    public ReactionStationPad startPad;     // punching this in Idle starts a run
    public TMP_Text hud;                    // debug HUD

    [Header("Run tuning")]
    public float runDuration = 60f;         // s
    public float readyDuration = 1.5f;      // s "READY" phase before the run begins
    public float nextPadDelay = 0.25f;      // s between a hit and the next pad lighting
    public float minGlobalHitInterval = 0.1f; // s; hits register at most this often
    public int recentPadCount = 2;          // never reuse one of the last N pads

    [Header("Scoring (by reaction time, s)")]
    public float fastReaction = 0.35f;      // <= this scores maxPoints
    public float slowReaction = 1.5f;       // >= this scores minPoints; linear between
    public int maxPoints = 100;
    public int minPoints = 25;

    [Header("Grades (by average reaction time, s)")]
    public float gradeS = 0.45f;
    public float gradeA = 0.65f;
    public float gradeB = 0.9f;
    public float gradeC = 1.2f;

    [Header("Events")]
    public UdonSharpBehaviour runCompleteTarget;
    public string runCompleteEvent = "OnRunComplete";
    public UdonSharpBehaviour padActivatedTarget;
    public string padActivatedEvent = "OnPadActivated";
    public UdonSharpBehaviour hitTarget;
    public string hitEvent = "OnHit";

    [Header("Results (last completed run)")]
    public int lastScore;
    public int lastHits;
    public float lastAvgReaction;
    public float lastFastestReaction;
    public string lastGrade = "";
    public float lastDuration;

    private int state = STATE_IDLE;
    private float readyEndTime;
    private float nextPadAt;
    private float runEndTime;
    private int activePad = -1;
    private float lastAcceptedHitTime = -999f;
    private int runScore;
    private int runHits;
    private float runReactionSum;
    private float runFastestReaction = -1f;
    private bool hasResults;
    private int[] recentPads;   // ring buffer of the last recentPadCount used pad indices
    private int recentHead;
    private int recentCount;

    void Start()
    {
        int size = recentPadCount < 1 ? 1 : recentPadCount;
        recentPads = new int[size];
        recentHead = 0;
        recentCount = 0;
        if (pads != null)
        {
            for (int i = 0; i < pads.Length; i++)
            {
                if (pads[i] == null) continue;
                pads[i].padIndex = i;
                if (pads[i].manager == null) pads[i].manager = this;
                if (pads[i].punchTarget != null && pads[i].punchTarget.hitEventReceiver == null)
                {
                    pads[i].punchTarget.hitEventReceiver = pads[i];
                    pads[i].punchTarget.hitEventName = "OnPadHit";
                }
                pads[i].SetLit(false);
            }
        }
        if (startPad != null)
        {
            if (startPad.manager == null) startPad.manager = this;
            if (startPad.punchTarget != null && startPad.punchTarget.hitEventReceiver == null)
            {
                startPad.punchTarget.hitEventReceiver = startPad;
                startPad.punchTarget.hitEventName = "OnPadHit";
            }
            startPad.SetLit(true);
        }
        UpdateHud();
    }

    void Update()
    {
        if (state == STATE_IDLE) return;
        // local player gone mid-run: silently abort, no results published
        if (Networking.LocalPlayer == null) { AbortRun(); return; }
        if (state == STATE_RUNNING)
        {
            // strictly greater: a punch on the run's final frame still counts,
            // discarding happens only after the punch checks
            if (Time.time > runEndTime) { EndRun(); return; }
            if (Time.frameCount % 6 == 0) UpdateHud();
        }
    }

    public bool IsStationBusy() { return state != STATE_IDLE; }

    // Entry point for every pad hit (routed from ReactionStationPad.OnPadHit).
    public void HandlePadHit(ReactionStationPad pad)
    {
        if (pad == null) return;
        if (state == STATE_IDLE)
        {
            if (pad.isStartPad) TryStartRun();
            return;
        }
        if (state != STATE_RUNNING) return;          // READY phase: ignore
        // only the lit pad scores; a dedicated START pad (padIndex -1) never
        // matches activePad, so punching START mid-run does nothing — and an
        // arc pad marked isStartPad still scores normally when it lights up
        if (activePad < 0 || pad.padIndex != activePad) return;
        if (pad.WasScored()) return;                 // same pad scores at most once
        float now = Time.time;
        if (now - lastAcceptedHitTime < minGlobalHitInterval) return;

        lastAcceptedHitTime = now;
        pad.MarkScored();
        pad.SetLit(false);
        activePad = -1;

        float reaction = now - pad.LitAt();
        int points = ScoreForReaction(reaction);
        runScore += points;
        runHits++;
        runReactionSum += reaction;
        if (runFastestReaction < 0f || reaction < runFastestReaction) runFastestReaction = reaction;

        FireEvent(hitTarget, hitEvent);
        UpdateHud();
        nextPadAt = now + nextPadDelay;
        SendCustomEventDelayedSeconds("_ActivateNextPad", nextPadDelay);
    }

    private void TryStartRun()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null || !player.IsUserInVR()) return;  // desktop players are not playable
        if (pads == null || pads.Length == 0) return;
        StartRun();
    }

    private void StartRun()
    {
        state = STATE_READY;
        runScore = 0;
        runHits = 0;
        runReactionSum = 0f;
        runFastestReaction = -1f;
        activePad = -1;
        lastAcceptedHitTime = -999f;
        recentHead = 0;
        recentCount = 0;
        readyEndTime = Time.time + readyDuration;
        if (pads != null)
            for (int i = 0; i < pads.Length; i++)
                if (pads[i] != null) pads[i].SetLit(false);
        if (startPad != null) startPad.SetLit(false);
        SendCustomEventDelayedSeconds("_BeginRun", readyDuration);
        UpdateHud();
    }

    // delayed by readyDuration from StartRun; the timestamp guard rejects stale
    // events from an aborted previous run
    public void _BeginRun()
    {
        if (state != STATE_READY) return;
        if (Time.time < readyEndTime - 0.001f) return;
        state = STATE_RUNNING;
        runEndTime = Time.time + runDuration;
        ActivateNextPad();
        UpdateHud();
    }

    // delayed by nextPadDelay after each hit; same stale-event guard
    public void _ActivateNextPad()
    {
        if (state != STATE_RUNNING || activePad >= 0) return;
        if (Time.time < nextPadAt - 0.001f) return;
        ActivateNextPad();
    }

    private void ActivateNextPad()
    {
        int idx = PickNextPad();
        if (idx < 0) { EndRun(); return; }
        activePad = idx;
        pads[idx].SetLit(true);
        RecordRecent(idx);
        FireEvent(padActivatedTarget, padActivatedEvent);
        UpdateHud();
    }

    // random, but never the pad that just played, never adjacent to it on the
    // arc, and never one of the last recentPadCount pads used
    private int PickNextPad()
    {
        int n = pads.Length;
        int last = -1;
        if (recentCount > 0)
            last = recentPads[(recentHead - 1 + recentPads.Length) % recentPads.Length];
        for (int attempt = 0; attempt < 64; attempt++)
        {
            int idx = Random.Range(0, n);
            if (IsPadAllowed(idx, last, n)) return idx;
        }
        for (int idx = 0; idx < n; idx++)
            if (IsPadAllowed(idx, last, n)) return idx;
        return -1;
    }

    private bool IsPadAllowed(int idx, int last, int n)
    {
        if (idx == last) return false;
        if (last >= 0)
        {
            if (idx == (last + 1) % n) return false;
            if (idx == (last - 1 + n) % n) return false;
        }
        for (int i = 0; i < recentCount; i++)
            if (recentPads[i] == idx) return false;
        return true;
    }

    private void RecordRecent(int idx)
    {
        recentPads[recentHead] = idx;
        recentHead = (recentHead + 1) % recentPads.Length;
        if (recentCount < recentPads.Length) recentCount++;
    }

    private void EndRun()
    {
        if (state != STATE_RUNNING) return;
        if (activePad >= 0 && pads[activePad] != null) pads[activePad].SetLit(false);
        activePad = -1;
        lastScore = runScore;
        lastHits = runHits;
        lastDuration = runDuration;
        lastAvgReaction = runHits > 0 ? runReactionSum / runHits : 0f;
        lastFastestReaction = runHits > 0 ? runFastestReaction : 0f;
        lastGrade = GradeFor(lastAvgReaction, runHits);
        hasResults = true;
        state = STATE_IDLE;   // instantly back to Idle; START can be re-punched
        if (startPad != null) startPad.SetLit(true);
        FireEvent(runCompleteTarget, runCompleteEvent);
        UpdateHud();
    }

    private void AbortRun()
    {
        state = STATE_IDLE;
        activePad = -1;
        if (pads != null)
            for (int i = 0; i < pads.Length; i++)
                if (pads[i] != null) pads[i].SetLit(false);
        if (startPad != null) startPad.SetLit(true);
        UpdateHud();
    }

    // 100 at <= fastReaction, 25 at >= slowReaction, linear in between
    private int ScoreForReaction(float reaction)
    {
        if (reaction <= fastReaction) return maxPoints;
        if (reaction >= slowReaction) return minPoints;
        float span = slowReaction - fastReaction;
        if (span <= 0f) return minPoints;
        float t = (reaction - fastReaction) / span;
        return Mathf.RoundToInt(Mathf.Lerp(maxPoints, minPoints, t));
    }

    private string GradeFor(float avgReaction, int hits)
    {
        if (hits <= 0) return "D";
        if (avgReaction < gradeS) return "S";
        if (avgReaction < gradeA) return "A";
        if (avgReaction < gradeB) return "B";
        if (avgReaction < gradeC) return "C";
        return "D";
    }

    private void FireEvent(UdonSharpBehaviour target, string eventName)
    {
        if (target != null && !string.IsNullOrEmpty(eventName))
            target.SendCustomEvent(eventName);
    }

    private void UpdateHud()
    {
        if (hud == null) return;
        if (state == STATE_IDLE)
        {
            if (hasResults)
                hud.text = string.Format(
                    "SCORE {0}  HITS {1}\nAVG {2:0.000}s  BEST {3:0.000}s  GRADE {4}\nPUNCH START",
                    lastScore, lastHits, lastAvgReaction, lastFastestReaction, lastGrade);
            else
                hud.text = "PUNCH START";
        }
        else if (state == STATE_READY)
        {
            hud.text = "READY";
        }
        else
        {
            hud.text = string.Format("TIME {0:0.0}  SCORE {1}  HITS {2}",
                Mathf.Max(0f, runEndTime - Time.time), runScore, runHits);
        }
    }
}
