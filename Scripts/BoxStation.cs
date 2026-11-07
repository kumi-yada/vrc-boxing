using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BoxStation : UdonSharpBehaviour
{
    public BoxPad leftPad;
    public BoxPad rightPad;
    public TMP_Text statusText; // TMP_Text base: TMP properties are only exposed on it
    public GameObject canvas;

    public float sideSwitchChance = 0.8f;

    [UdonSynced] public bool isInitialized = false;
    [UdonSynced] private bool isGameActive = false;
    [UdonSynced] private bool activeIsLeft = false;

    // ---- punch recognition / form judging (M1) ----
    // Thresholds are tuned by playtesting in VR while watching the debug
    // panel; see Assets/Scripts/Punch/TUNING.md for what each value means.
    public bool punchSystemEnabled = true;
    public bool debugPanelVisible = true;
    public int debugPanelLines = 8;
    public float minPunchSpeed = 2.0f;          // m/s; below this a touch is not a punch
    public float fistRadius = 0.10f;            // m, swept-sphere radius of a fist
    public float hitCooldown = 0.25f;           // s, per hand: one swing = one hit
    public float hipWindow = 0.30f;             // s of head-yaw history for hip drive
    public float hipDriveMinDeg = 10f;          // min head yaw into the punch
    public bool hipRequireDirection = true;     // yaw must rotate toward the target
    public float guardWindow = 0.08f;           // s around impact for the guard check
    public float guardMaxDist = 0.28f;          // m, other hand to chin
    public float telegraphWindow = 0.45f;       // s of history scanned for a wind-up
    public float telegraphExcludeFinal = 0.12f; // final acceleration is not a wind-up
    public float telegraphMinDist = 0.12f;      // m of pull-back along the punch axis
    public float telegraphDip = 0.08f;          // m of dip before striking
    public float retractWindow = 0.35f;         // s allowed to return to guard
    public float retractMaxDist = 0.32f;        // m, hand to chin counts as retracted
    public float retractMinBack = 0.12f;        // m back along the punch axis
    public float maxSpeedForFeedback = 8f;      // m/s that maps to full sound/haptics
    public float sloppyScoreMult = 0.6f;

    // ---- runtime state for the punch system ----
    // UdonSharp cannot instantiate user classes, so all mutable state lives in
    // these arrays and the Punch/* helpers are static methods operating on them.
    // ring: 0 = left fist, 1 = right fist, 2 = head
    [System.NonSerialized] public Vector3[][] ringPos;
    [System.NonSerialized] public Vector3[][] ringVel;
    [System.NonSerialized] public Vector3[][] ringAux; // head: forward vector
    [System.NonSerialized] public float[][] ringTime;
    [System.NonSerialized] public int[] ringHead;
    [System.NonSerialized] public int[] ringCount;
    [System.NonSerialized] public Vector3[] ringPrev;
    [System.NonSerialized] public bool[] ringHasPrev;

    [System.NonSerialized] public GameObject[] targetGo;
    [System.NonSerialized] public Collider[] targetCol;
    [System.NonSerialized] public PunchTargetType[] targetType;
    [System.NonSerialized] public PunchHand[] targetExpected;
    [System.NonSerialized] public bool[] targetTele;
    [System.NonSerialized] public bool[] targetDip;
    [System.NonSerialized] public string[] targetLabel;
    [System.NonSerialized] public BoxPad[] targetPad;
    [System.NonSerialized] public Vector3[] targetBaseScale;
    [System.NonSerialized] public float[] targetSquash;
    [System.NonSerialized] public int targetCount; // collider lookup is a linear scan over targetCol

    [System.NonSerialized] public long[] latchKeys;   // one swing = one hit
    [System.NonSerialized] public int[] latchTargets;
    [System.NonSerialized] public int latchCount;

    [System.NonSerialized] public float[] handCooldown;

    // recent punch records (ring buffer of parallel arrays)
    [System.NonSerialized] public float[] recTime;
    [System.NonSerialized] public PunchHand[] recHand;
    [System.NonSerialized] public PunchTargetType[] recType;
    [System.NonSerialized] public string[] recName;
    [System.NonSerialized] public Vector3[] recHit;
    [System.NonSerialized] public Vector3[] recDir;
    [System.NonSerialized] public float[] recSpeed;
    [System.NonSerialized] public float[] recScore;
    [System.NonSerialized] public float[] recYaw;
    [System.NonSerialized] public float[] recGuard;
    [System.NonSerialized] public float[] recPull;
    [System.NonSerialized] public float[] recDip;
    [System.NonSerialized] public bool[] recNoHip;
    [System.NonSerialized] public bool[] recGuardDown;
    [System.NonSerialized] public bool[] recTele;
    [System.NonSerialized] public bool[] recNoRet;
    [System.NonSerialized] public bool[] recRetResolved;
    [System.NonSerialized] public PunchQuality[] recQuality;
    [System.NonSerialized] public int recHead;
    [System.NonSerialized] public int recCount;

    [System.NonSerialized] public int[] pendingRecs; // records awaiting retraction verdict
    [System.NonSerialized] public int pendingCount;

    // per-punch haptic feedback
    [System.NonSerialized] public VRCPlayerApi fbPlayer;

    [System.NonSerialized] public GameObject panelRoot;
    [System.NonSerialized] public TMP_Text panelText;

    [System.NonSerialized] public Collider[] overlapBuf;

    private PadPosition[] allPositions = new PadPosition[] {
        PadPosition.Jab, PadPosition.Jab, PadPosition.Jab,
        PadPosition.Hook, PadPosition.Hook,
        PadPosition.UpperCut,
    };

    void Start()
    {
        if (Networking.IsOwner(gameObject))
        {
            SetInitialized(false);
            leftPad.InitializePad(PadPosition.Jab, true);
            rightPad.InitializePad(PadPosition.Jab, false);
        }
        PunchSystem.Initialize(this);
    }

    void Update()
    {
        if (punchSystemEnabled && Networking.LocalPlayer != null)
            PunchSystem.UpdateLocal(this, Networking.LocalPlayer);
        if (Input.GetKeyDown(KeyCode.P))
            PunchDebugPanel.Toggle(this);
    }

    public void ToggleDebugPanel()
    {
        PunchDebugPanel.Toggle(this);
    }

    public void PrepareGame(VRCPlayerApi player)
    {
        if (isInitialized)
            return;

        statusText.text = "Player: " + player.displayName;
        SetInitialized(true);
        Networking.SetOwner(player, gameObject);
        Networking.SetOwner(player, leftPad.gameObject);
        Networking.SetOwner(player, rightPad.gameObject);
        RequestSerialization();
    }

    public void StartGame(VRCPlayerApi player)
    {
        if (isGameActive)
            return;

        statusText.text = "Starting game";
        isGameActive = true;
        RequestSerialization();

        bool startLeft = Random.value > 0.5f;
        ShowPad(startLeft);
    }

    public void PadHit(bool isLeft, float force)
    {
        if (!isGameActive)
        {
            StartGame(Networking.GetOwner(leftPad.gameObject));
            return;
        }

        if (activeIsLeft != isLeft) return;

        statusText.text = "Hit with force: " + force;
        var toggle = Random.value < sideSwitchChance;
        ShowPad(toggle ? !activeIsLeft : activeIsLeft);
    }

    private PadPosition RandomPosition()
    {
        int idx = Random.Range(0, allPositions.Length);
        return allPositions[idx];
    }

    private void ShowPad(bool showLeft)
    {
        activeIsLeft = showLeft;
        PadPosition pos = RandomPosition();

        if (showLeft)
        {
            leftPad.MovePad(pos);
            leftPad.SetPadActive(true);
            rightPad.SetPadActive(false);
        }
        else
        {
            rightPad.MovePad(pos);
            rightPad.SetPadActive(true);
            leftPad.SetPadActive(false);
        }
    }

    private void SetInitialized(bool init)
    {
        isInitialized = init;
        UpdateCanvasActive();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        UpdateCanvasActive();
    }

    private void UpdateCanvasActive()
    {
        canvas.SetActive(isInitialized);
    }
}
