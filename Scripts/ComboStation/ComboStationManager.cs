using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Combo Cutter Station: step on the mat -> guard calibration -> a fixed ladder
// of called combos punched into one cyan zone on the Holo-Coach -> accuracy %
// and C/B/A/S grade -> punch the restart pad. Fully local (no [UdonSynced]),
// VR-only; hits arrive via the zone's PunchTarget hook through
// ComboStationTarget.OnTargetHit. Timed phases are driven from Update against
// phaseEndsAt so a stale delayed event can never fire into a newer run.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ComboStationManager : UdonSharpBehaviour
{
    private const int STATE_IDLE = 0;
    private const int STATE_CALIBRATING = 1;
    private const int STATE_ANNOUNCE = 2;
    private const int STATE_STEP = 3;
    private const int STATE_GAP = 4;
    private const int STATE_BREAK = 5;
    private const int STATE_LANDED = 6;
    private const int STATE_RESULTS = 7;

    private const int PERFECT_POINTS = 100;
    private const int GOOD_POINTS = 50;

    [Header("Wiring")]
    public ComboStationTarget zone;
    public ComboStationTarget restartPad;
    public ComboHoloCoach coach;
    public ComboMarquee marquee;
    public ComboCuePlayer cues;
    public TMP_Text scoreboard;
    public Transform matCenter;

    [Header("Mat")]
    public float matRadius = 0.6f;
    public float matExitRadius = 0.9f;
    public float matExitGrace = 1.5f;

    [Header("Guard calibration")]
    public float calibrationDuration = 3f;
    public float calibrationTolerance = 0.1f;
    public float calibrationMaxMove = 0.2f;
    public float guardMaxDrop = 0.6f;
    public float guardRadius = 0.15f;

    [Header("Ladder (J = jab, H = hook, U = uppercut)")]
    public string[] comboNames = { "Jab-Hook", "Jab-Uppercut", "Hook-Uppercut", "Jab-Hook-Uppercut", "Uppercut-Hook-Jab", "Hook-Jab-Uppercut" };
    public string[] comboSequences = { "JH", "JU", "HU", "JHU", "UHJ", "HJU" };

    [Header("Form grading (m/s)")]
    public float ignoreBelowSpeed = 1.5f;
    public float minPunchSpeed = 2.5f;
    public float perfectSpeed = 4.5f;

    [Header("Timing (s)")]
    public float announceDuration = 1.2f;
    public float stepGap = 0.12f;
    public float breakDuration = 0.9f;
    public float landedDuration = 0.9f;
    public float stallTimeout = 10f;
    public float restartDebounce = 1f;
    public float flashDuration = 0.35f;
    public float guardBobInterval = 0.4f;

    [Header("Grades (accuracy %)")]
    public float gradeS = 95f;
    public float gradeA = 80f;
    public float gradeB = 60f;

    [Header("Colors")]
    public Color successColor = new Color(0.2f, 1f, 0.35f, 0.9f);
    public Color breakColor = new Color(1f, 0.15f, 0.15f, 0.9f);

    [Header("Events")]
    public UdonSharpBehaviour runCompleteTarget;
    public string runCompleteEvent = "OnComboRunComplete";

    [HideInInspector] public float lastAccuracy;
    [HideInInspector] public string lastGrade = "";
    [HideInInspector] public int lastPoints;
    [HideInInspector] public int lastPunches;
    [HideInInspector] public int lastBestStreak;

    private int state = STATE_IDLE;
    private float phaseEndsAt;

    private int[][] comboTypes;
    private int comboCount;

    private bool onMat;
    private float offMatSince = -1f;
    private bool vrWarned;

    private bool guardCaptured;
    private Vector3 guardL;
    private Vector3 guardR;
    private Vector3 sampleL;
    private Vector3 sampleR;
    private int calSamples;
    private Vector3 calSumL;
    private Vector3 calSumR;
    private float calStartedAt;
    private Vector3 calRootStart;
    private string calMessage = "";
    private bool outOfGuard;
    private float lastGuardBob = -999f;

    private int comboIndex;
    private int step;
    private float stepLitAt;
    private int runPoints;
    private int runPunches;
    private int streak;
    private int bestStreak;
    private int comboTries;
    private string lastJudgement = "";
    private float restartArmedAt;
    private bool hasResults;

    void Start()
    {
        ParseLadder();
        WireTarget(zone);
        WireTarget(restartPad);
        if (zone != null) zone.SetLit(false);
        if (restartPad != null) restartPad.SetLit(false);
        if (marquee != null) marquee.Clear("STEP ON THE MAT");
        UpdateScoreboard();
    }

    private void WireTarget(ComboStationTarget t)
    {
        if (t == null) return;
        if (t.manager == null) t.manager = this;
        if (t.punchTarget == null) t.punchTarget = t.GetComponent<PunchTarget>();
        if (t.punchTarget != null && t.punchTarget.hitEventReceiver == null)
        {
            t.punchTarget.hitEventReceiver = t;
            t.punchTarget.hitEventName = "OnTargetHit";
        }
    }

    private void ParseLadder()
    {
        int n = comboSequences != null ? comboSequences.Length : 0;
        comboTypes = new int[n][];
        comboCount = 0;
        for (int i = 0; i < n; i++)
        {
            string seq = comboSequences[i];
            if (string.IsNullOrEmpty(seq)) continue;
            int[] types = new int[seq.Length];
            bool ok = true;
            for (int c = 0; c < seq.Length; c++)
            {
                types[c] = TypeForCode(seq[c]);
                if (types[c] == (int)PunchType.None) ok = false;
            }
            if (!ok)
            {
                Debug.LogWarning("[ComboStation] Skipping combo with bad sequence: " + seq);
                continue;
            }
            comboTypes[comboCount++] = types;
        }
    }

    private int TypeForCode(char c)
    {
        if (c == 'J' || c == 'j') return (int)PunchType.Jab;
        if (c == 'H' || c == 'h') return (int)PunchType.Hook;
        if (c == 'U' || c == 'u') return (int)PunchType.Uppercut;
        return (int)PunchType.None;
    }

    void Update()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null)
        {
            if (state != STATE_IDLE) GoIdle();
            return;
        }

        UpdateMat(player);
        float now = Time.time;

        if (state == STATE_IDLE)
        {
            if (!onMat)
            {
                if (vrWarned) { vrWarned = false; UpdateScoreboard(); }
                return;
            }
            if (player.IsUserInVR())
            {
                BeginCalibration(player);
            }
            else if (!vrWarned)
            {
                vrWarned = true;
                Cue(ComboCuePlayer.CUE_VR_ONLY);
                UpdateScoreboard();
            }
            return;
        }

        if (!onMat)
        {
            // results stay up after a finished run; an unfinished one is dropped unpublished
            GoIdle();
            return;
        }

        if (state == STATE_CALIBRATING) { TickCalibration(player); return; }

        if (state == STATE_RESULTS)
        {
            ArmRestartPadWhenReady(now);
            return;
        }

        ArmRestartPadWhenReady(now);
        TickGuard(player, now);

        if (state == STATE_ANNOUNCE)
        {
            if (now >= phaseEndsAt) LightStep();
        }
        else if (state == STATE_STEP)
        {
            if (now - stepLitAt >= stallTimeout)
            {
                // a stall is a "no contact" MISS, so it costs one punch of accuracy
                runPunches++;
                Break("STALL");
            }
        }
        else if (state == STATE_GAP)
        {
            if (now >= phaseEndsAt) LightStep();
        }
        else if (state == STATE_BREAK)
        {
            if (now >= phaseEndsAt) Announce();
        }
        else if (state == STATE_LANDED)
        {
            if (now >= phaseEndsAt)
            {
                comboIndex++;
                comboTries = 0;
                if (comboIndex >= comboCount) FinishRun();
                else Announce();
            }
        }

        if (Time.frameCount % 6 == 0) UpdateScoreboard();
    }

    private void UpdateMat(VRCPlayerApi player)
    {
        if (matCenter == null) { onMat = false; return; }
        Vector3 d = player.GetPosition() - matCenter.position;
        bool vertOk = Mathf.Abs(d.y) < 1f;
        d.y = 0f;
        float dist = d.magnitude;
        if (!onMat)
        {
            onMat = vertOk && dist <= matRadius;
            offMatSince = -1f;
            return;
        }
        // lunging into a punch can swing the player capsule past the mat edge,
        // so only a sustained exit counts as leaving
        if (vertOk && dist <= matExitRadius)
        {
            offMatSince = -1f;
            return;
        }
        if (offMatSince < 0f) offMatSince = Time.time;
        if (Time.time - offMatSince >= matExitGrace) onMat = false;
    }

    private bool SampleGuard(VRCPlayerApi player)
    {
        Vector3 head = player.GetBonePosition(HumanBodyBones.Head);
        Vector3 lh = player.GetBonePosition(HumanBodyBones.LeftHand);
        Vector3 rh = player.GetBonePosition(HumanBodyBones.RightHand);
        if (head == Vector3.zero || lh == Vector3.zero || rh == Vector3.zero) return false;
        Quaternion inv = Quaternion.Inverse(player.GetRotation());
        sampleL = inv * (lh - head);
        sampleR = inv * (rh - head);
        return true;
    }

    private void BeginCalibration(VRCPlayerApi player)
    {
        state = STATE_CALIBRATING;
        guardCaptured = false;
        if (zone != null) zone.SetLit(false);
        if (restartPad != null) restartPad.SetLit(false);
        Cue(ComboCuePlayer.CUE_CALIBRATE_START);
        if (marquee != null) marquee.Clear("RAISE YOUR GUARD");
        RestartCapture(player, "HOLD A RELAXED GUARD");
    }

    private void RestartCapture(VRCPlayerApi player, string message)
    {
        // only cue when a capture in progress was thrown away, so a dropped guard
        // that fails every frame does not spam the retry sound
        if (calSamples > 10) Cue(ComboCuePlayer.CUE_CALIBRATE_RETRY);
        calSamples = 0;
        calSumL = Vector3.zero;
        calSumR = Vector3.zero;
        calStartedAt = Time.time;
        calRootStart = player.GetPosition();
        calMessage = message;
        UpdateScoreboard();
    }

    private void TickCalibration(VRCPlayerApi player)
    {
        if (!SampleGuard(player)) { RestartCapture(player, "TRACKING LOST"); return; }

        Vector3 moved = player.GetPosition() - calRootStart;
        moved.y = 0f;
        if (moved.magnitude > calibrationMaxMove) { RestartCapture(player, "STAND STILL"); return; }
        if (sampleL.y < -guardMaxDrop || sampleR.y < -guardMaxDrop) { RestartCapture(player, "RAISE YOUR GUARD"); return; }

        if (calSamples >= 10)
        {
            Vector3 meanL = calSumL / calSamples;
            Vector3 meanR = calSumR / calSamples;
            if (Vector3.Distance(sampleL, meanL) > calibrationTolerance
                || Vector3.Distance(sampleR, meanR) > calibrationTolerance)
            {
                RestartCapture(player, "HOLD STILL");
                return;
            }
        }

        calSumL += sampleL;
        calSumR += sampleR;
        calSamples++;

        if (Time.time - calStartedAt >= calibrationDuration && calSamples >= 10)
        {
            guardL = calSumL / calSamples;
            guardR = calSumR / calSamples;
            guardCaptured = true;
            Cue(ComboCuePlayer.CUE_CALIBRATE_DONE);
            StartRun();
            return;
        }
        if (Time.frameCount % 6 == 0) UpdateScoreboard();
    }

    private void TickGuard(VRCPlayerApi player, float now)
    {
        if (!guardCaptured || !SampleGuard(player)) return;
        bool inGuard = Vector3.Distance(sampleL, guardL) <= guardRadius
                    && Vector3.Distance(sampleR, guardR) <= guardRadius;
        if (!inGuard) { outOfGuard = true; return; }
        if (!outOfGuard) return;
        outOfGuard = false;
        if (now - lastGuardBob < guardBobInterval) return;
        lastGuardBob = now;
        Cue(ComboCuePlayer.CUE_GUARD_READY);
        if (coach != null) coach.GuardBob();
    }

    private void StartRun()
    {
        comboIndex = 0;
        runPoints = 0;
        runPunches = 0;
        streak = 0;
        bestStreak = 0;
        comboTries = 0;
        lastJudgement = "";
        outOfGuard = false;
        restartArmedAt = Time.time + restartDebounce;
        if (restartPad != null) restartPad.SetLit(false);
        if (comboCount == 0) { FinishRun(); return; }
        Announce();
    }

    private void Announce()
    {
        state = STATE_ANNOUNCE;
        step = 0;
        comboTries++;
        phaseEndsAt = Time.time + announceDuration;
        if (zone != null) zone.SetLit(false);
        Cue(ComboCuePlayer.CUE_ANNOUNCE);
        if (marquee != null)
        {
            int[] types = comboTypes[comboIndex];
            marquee.Announce(string.Format("COMBO {0}/{1}\n{2}", comboIndex + 1, comboCount, ComboName(comboIndex)), types, types.Length);
        }
        UpdateScoreboard();
    }

    private void LightStep()
    {
        state = STATE_STEP;
        stepLitAt = Time.time;
        Cue(ComboCuePlayer.CUE_STEP_LIT);
        if (zone != null) zone.SetLit(true);
        if (marquee != null) marquee.SetStep(step);
    }

    public void HandleTargetHit(ComboStationTarget target)
    {
        if (target == null) return;
        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null || !player.IsUserInVR()) return;

        if (target == restartPad) { HandleRestart(); return; }
        if (target != zone || zone.punchTarget == null) return;

        float speed = zone.punchTarget.LastPunchSpeed;
        // a glove drifting through the zone is not a thrown punch at all
        if (speed < ignoreBelowSpeed) return;

        if (state == STATE_STEP)
        {
            GradePunch((int)zone.punchTarget.LastPunchType, speed);
        }
        else if (state == STATE_ANNOUNCE || state == STATE_GAP)
        {
            runPunches++;
            Break("EARLY");
        }
    }

    private void GradePunch(int type, float speed)
    {
        runPunches++;
        int expected = comboTypes[comboIndex][step];
        if (type != expected)
        {
            Break(string.Format("{0} - WANTED {1}", PunchClassifier.TypeName((PunchType)type), PunchClassifier.TypeName((PunchType)expected)));
            return;
        }
        if (speed < minPunchSpeed)
        {
            Break(string.Format("TOO SLOW {0:0.0} m/s", speed));
            return;
        }

        bool perfect = speed >= perfectSpeed;
        runPoints += perfect ? PERFECT_POINTS : GOOD_POINTS;
        streak++;
        if (streak > bestStreak) bestStreak = streak;
        lastJudgement = string.Format("{0}  {1}  {2:0.0} m/s", perfect ? "PERFECT" : "GOOD", PunchClassifier.TypeName((PunchType)type), speed);

        Cue(perfect ? ComboCuePlayer.CUE_PERFECT : ComboCuePlayer.CUE_GOOD);
        if (zone != null) zone.Flash(successColor, flashDuration);
        if (perfect && coach != null) coach.Flash(successColor, flashDuration);

        step++;
        if (step >= comboTypes[comboIndex].Length)
        {
            ComboLanded();
            return;
        }
        state = STATE_GAP;
        phaseEndsAt = Time.time + stepGap;
        if (zone != null) zone.SetLit(false);
        if (marquee != null) marquee.SetStep(step);
        UpdateScoreboard();
    }

    // the miss is already counted in runPunches by the caller; a break never
    // ends the run, it only re-announces the same combo after breakDuration
    private void Break(string reason)
    {
        state = STATE_BREAK;
        phaseEndsAt = Time.time + breakDuration;
        streak = 0;
        lastJudgement = "MISS  " + reason;
        Cue(ComboCuePlayer.CUE_MISS);
        if (zone != null)
        {
            zone.SetLit(false);
            zone.Flash(breakColor, flashDuration);
        }
        if (coach != null)
        {
            coach.Flash(breakColor, flashDuration);
            coach.Mock();
        }
        if (marquee != null) marquee.MarkBroken(step);
        UpdateScoreboard();
    }

    private void ComboLanded()
    {
        state = STATE_LANDED;
        phaseEndsAt = Time.time + landedDuration;
        Cue(ComboCuePlayer.CUE_COMBO_LANDED);
        if (zone != null) zone.SetLit(false);
        if (coach != null) coach.Flash(successColor, flashDuration * 2f);
        if (marquee != null)
        {
            marquee.MarkAllDone();
            marquee.SetTitle(string.Format("COMBO {0}/{1}\nLANDED!", comboIndex + 1, comboCount));
        }
        UpdateScoreboard();
    }

    private void FinishRun()
    {
        lastPoints = runPoints;
        lastPunches = runPunches;
        lastBestStreak = bestStreak;
        lastAccuracy = AccuracyPercent(runPoints, runPunches);
        lastGrade = GradeFor(lastAccuracy);
        hasResults = true;
        state = STATE_RESULTS;
        restartArmedAt = Time.time + restartDebounce;
        if (zone != null) zone.SetLit(false);
        if (restartPad != null) restartPad.SetLit(false);
        Cue(ComboCuePlayer.CUE_RUN_COMPLETE);
        if (marquee != null) marquee.Clear("GRADE " + lastGrade + "\nPUNCH RESTART");
        UpdateScoreboard();
        if (runCompleteTarget != null && !string.IsNullOrEmpty(runCompleteEvent))
            runCompleteTarget.SendCustomEvent(runCompleteEvent);
    }

    private void HandleRestart()
    {
        if (state == STATE_IDLE || state == STATE_CALIBRATING) return;
        if (Time.time < restartArmedAt) return;
        if (!guardCaptured) return;
        Cue(ComboCuePlayer.CUE_RESTART);
        if (restartPad != null) restartPad.Flash(successColor, flashDuration);
        StartRun();
    }

    private void ArmRestartPadWhenReady(float now)
    {
        if (restartPad == null || restartPad.IsLit() || now < restartArmedAt) return;
        restartPad.SetLit(true);
    }

    private void GoIdle()
    {
        state = STATE_IDLE;
        guardCaptured = false;
        calSamples = 0;
        vrWarned = false;
        if (zone != null) zone.SetLit(false);
        if (restartPad != null) restartPad.SetLit(false);
        if (marquee != null) marquee.Clear("STEP ON THE MAT");
        UpdateScoreboard();
    }

    public float AccuracyPercent(int points, int punches)
    {
        if (punches <= 0) return 0f;
        return 100f * points / (PERFECT_POINTS * (float)punches);
    }

    public string GradeFor(float accuracy)
    {
        if (accuracy >= gradeS) return "S";
        if (accuracy >= gradeA) return "A";
        if (accuracy >= gradeB) return "B";
        return "C";
    }

    private string ComboName(int i)
    {
        if (comboNames != null && i < comboNames.Length && !string.IsNullOrEmpty(comboNames[i])) return comboNames[i];
        return "COMBO " + (i + 1);
    }

    private void Cue(int cue)
    {
        if (cues != null) cues.Play(cue);
    }

    private void UpdateScoreboard()
    {
        if (scoreboard == null) return;
        string results = hasResults
            ? string.Format("LAST  {0:0.0}%  GRADE {1}  BEST STREAK {2}", lastAccuracy, lastGrade, lastBestStreak)
            : "";

        if (state == STATE_IDLE)
        {
            VRCPlayerApi p = Networking.LocalPlayer;
            if (onMat && p != null && !p.IsUserInVR())
                scoreboard.text = "COMBO CUTTER\nVR ONLY\n" + results;
            else
                scoreboard.text = "COMBO CUTTER\nSTEP ON THE MAT\n" + results;
        }
        else if (state == STATE_CALIBRATING)
        {
            float left = Mathf.Max(0f, calibrationDuration - (Time.time - calStartedAt));
            scoreboard.text = string.Format("GUARD CALIBRATION\n{0}\n{1:0.0}s", calMessage, left);
        }
        else if (state == STATE_RESULTS)
        {
            scoreboard.text = string.Format(
                "ACCURACY {0:0.0}%\nGRADE {1}\nPOINTS {2} / {3}  BEST STREAK {4}\nPUNCH RESTART",
                lastAccuracy, lastGrade, lastPoints, lastPunches * PERFECT_POINTS, lastBestStreak);
        }
        else
        {
            string stall = state == STATE_STEP
                ? string.Format("  STALL {0:0}", Mathf.Max(0f, stallTimeout - (Time.time - stepLitAt)))
                : "";
            scoreboard.text = string.Format(
                "COMBO {0}/{1}  TRY {2}\nACCURACY {3:0.0}%  STREAK {4}{5}\n{6}",
                comboIndex + 1, comboCount, comboTries, AccuracyPercent(runPoints, runPunches), streak, stall, lastJudgement);
        }
    }
}
