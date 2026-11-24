using UdonSharp;
using UnityEngine;

// Per-pad component of the Reaction Station: owns the pad's lit/dark state and
// its lit-at timestamp, applies the placeholder visual (emission toggle),
// and routes PunchTarget hit events to the manager.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ReactionStationPad : UdonSharpBehaviour
{
    public bool isStartPad = false;
    public ReactionStationManager manager;
    public string emissionProperty = "_EmissionStrength"; // ToonStandard emission property driven while lit

    [HideInInspector] public int padIndex = -1; // set by the manager from the pads array; -1 = START pad
    [HideInInspector] public PunchTarget punchTarget;
    [HideInInspector] public Renderer padRenderer;

    private bool lit;
    private float litAt;
    private bool scored;

    void Start()
    {
        if (punchTarget == null) punchTarget = GetComponent<PunchTarget>();
        if (padRenderer == null) padRenderer = GetComponent<Renderer>();
        SetLit(isStartPad);
    }

    public bool IsLit() { return lit; }
    public float LitAt() { return litAt; }
    public bool WasScored() { return scored; }
    public void MarkScored() { scored = true; }

    public void SetLit(bool on)
    {
        lit = on;
        scored = false;
        if (on) litAt = Time.time;
        if (padRenderer != null)
            padRenderer.material.SetFloat(emissionProperty, on ? 1f : 0f);
    }

    // Hit hook routed from this pad's PunchTarget (hitEventReceiver = this pad,
    // hitEventName = "OnPadHit"); SendCustomEvent carries no parameters, so the
    // punch details are read from punchTarget.LastPunch* by the manager.
    public void OnPadHit()
    {
        if (manager != null) manager.HandlePadHit(this);
    }
}
