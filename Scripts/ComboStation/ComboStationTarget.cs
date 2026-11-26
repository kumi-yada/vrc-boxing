using UdonSharp;
using UnityEngine;

// Routes a PunchTarget hit hook (hitEventReceiver = this, hitEventName =
// "OnTargetHit") to the ComboStationManager and owns the placeholder look:
// cyan while lit, a timed green/red flash overlay, and a squash on hit.
// SendCustomEvent carries no parameters, so the manager reads the punch from
// punchTarget.LastPunch*.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ComboStationTarget : UdonSharpBehaviour
{
    public bool isRestartPad = false;
    public ComboStationManager manager;

    [Header("Look")]
    public Color litColor = new Color(0.1f, 0.95f, 1f, 0.75f);
    public Color darkColor = new Color(0.1f, 0.3f, 0.35f, 0.15f);
    public float emissionBoost = 2f;
    public float squashAmount = 0.25f;

    [HideInInspector] public PunchTarget punchTarget;
    [HideInInspector] public Renderer targetRenderer;

    private bool lit;
    private float litAt;
    private Color flashColor;
    private float flashUntil;
    private float flashDuration;
    private Vector3 baseScale;
    private float squash = 1f;
    private Material mat;

    void Start()
    {
        if (punchTarget == null) punchTarget = GetComponent<PunchTarget>();
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        if (targetRenderer != null) mat = targetRenderer.material;
        baseScale = transform.localScale;
        ApplyColor(lit ? litColor : darkColor);
    }

    void Update()
    {
        if (flashUntil > 0f)
        {
            if (Time.time >= flashUntil)
            {
                flashUntil = 0f;
                ApplyColor(lit ? litColor : darkColor);
            }
            else
            {
                float k = (flashUntil - Time.time) / Mathf.Max(flashDuration, 0.001f);
                ApplyColor(Color.Lerp(lit ? litColor : darkColor, flashColor, k));
            }
        }
        if (squash < 1f)
        {
            squash = Mathf.Min(1f, squash + Time.deltaTime * 6f);
            float k = Mathf.Sin(squash * Mathf.PI);
            transform.localScale = new Vector3(
                baseScale.x * (1f + squashAmount * 0.5f * k),
                baseScale.y * (1f + squashAmount * 0.5f * k),
                baseScale.z * (1f - squashAmount * k));
        }
    }

    public bool IsLit() { return lit; }
    public float LitAt() { return litAt; }

    public void SetLit(bool on)
    {
        if (on && !lit) litAt = Time.time;
        lit = on;
        if (flashUntil <= 0f) ApplyColor(on ? litColor : darkColor);
    }

    public void Flash(Color color, float duration)
    {
        flashColor = color;
        flashDuration = duration;
        flashUntil = Time.time + duration;
        ApplyColor(color);
    }

    public void OnTargetHit()
    {
        squash = 0f;
        if (manager != null) manager.HandleTargetHit(this);
    }

    private void ApplyColor(Color c)
    {
        if (mat == null) return;
        mat.color = c;
        mat.SetColor("_EmissionColor", new Color(c.r, c.g, c.b, 1f) * emissionBoost * c.a);
    }
}
