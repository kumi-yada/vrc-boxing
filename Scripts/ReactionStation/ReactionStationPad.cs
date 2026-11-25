using UdonSharp;
using UnityEngine;

// Per-pad component of the Reaction Station: owns the pad's lit/dark state and
// its lit-at timestamp, applies the placeholder visual (emission toggle) plus
// the squash animation on hit, and routes PunchTarget hit events to the manager.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ReactionStationPad : UdonSharpBehaviour
{
    public bool isStartPad = false;
    public ReactionStationManager manager;
    public string emissionProperty = "_EmissionStrength"; // ToonStandard emission property driven while lit

    [Header("Squash")]
    public float squashAmount = 0.35f; // peak y/z shrink on hit; x bulges proportionally

    [HideInInspector] public int padIndex = -1; // set by the manager from the pads array; -1 = START pad
    [HideInInspector] public PunchTarget punchTarget;
    [HideInInspector] public Renderer padRenderer;

    private const float SquashWidthRatio = 0.9f / 0.35f;
    private bool lit;
    private float litAt;
    private bool scored;
    private Vector3 baseScale;
    private float squash = 1f;

    void Start()
    {
        if (punchTarget == null) punchTarget = GetComponent<PunchTarget>();
        if (padRenderer == null) padRenderer = GetComponent<Renderer>();
        baseScale = transform.localScale;
        squashAmount = Mathf.Clamp01(squashAmount);
        SetLit(isStartPad);
    }

    void Update()
    {
        AnimateSquash();
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
        squash = 0f;
        if (manager != null) manager.HandlePadHit(this);
    }

    private void AnimateSquash()
    {
        if (squash >= 1f) return;
        squash = Mathf.Min(1f, squash + Time.deltaTime * 5f);
        float k = Mathf.Sin(squash * Mathf.PI); // 0 -> 1 -> 0
        transform.localScale = new Vector3(
            baseScale.x * (1f + squashAmount * SquashWidthRatio * k),
            baseScale.y * (1f - squashAmount * k),
            baseScale.z * (1f - squashAmount * k));
    }
}
