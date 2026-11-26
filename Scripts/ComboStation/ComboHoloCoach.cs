using UdonSharp;
using UnityEngine;

// The see-through sparring torso. Purely presentational: an idle bob-and-sway,
// a quick dip when the player returns to the calibrated guard, a mocking
// head-bob on a combo break, and a body color flash.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ComboHoloCoach : UdonSharpBehaviour
{
    public Transform body;
    public Transform head;
    public Renderer[] bodyRenderers;
    public Color baseColor = new Color(0.2f, 0.85f, 1f, 0.35f);

    [Header("Idle bob and sway")]
    public float bobHeight = 0.02f;
    public float bobRate = 1.7f;
    public float swayWidth = 0.035f;
    public float swayAngle = 4f;
    public float swayRate = 0.8f;

    [Header("Cues")]
    public float guardBobDepth = 0.04f;
    public float guardBobDuration = 0.3f;
    public float mockDuration = 0.9f;
    public float mockAngle = 18f;
    public float mockRate = 4f;

    private Vector3 bodyBase;
    private Quaternion bodyBaseRot;
    private Quaternion headBaseRot;
    private float guardBobStart = -999f;
    private float mockStart = -999f;
    private Color flashColor;
    private float flashUntil;
    private float flashDuration;
    private Material[] mats;

    void Start()
    {
        if (body == null) body = transform;
        bodyBase = body.localPosition;
        bodyBaseRot = body.localRotation;
        if (head != null) headBaseRot = head.localRotation;
        int n = bodyRenderers != null ? bodyRenderers.Length : 0;
        mats = new Material[n];
        for (int i = 0; i < n; i++)
            if (bodyRenderers[i] != null) mats[i] = bodyRenderers[i].material;
        ApplyColor(baseColor);
    }

    void Update()
    {
        float t = Time.time;
        float y = Mathf.Sin(t * bobRate * 2f * Mathf.PI) * bobHeight;
        float gb = (t - guardBobStart) / guardBobDuration;
        if (gb >= 0f && gb < 1f) y -= Mathf.Sin(gb * Mathf.PI) * guardBobDepth;
        float x = Mathf.Sin(t * swayRate * 2f * Mathf.PI) * swayWidth;
        float yaw = Mathf.Sin(t * swayRate * 2f * Mathf.PI + 0.7f) * swayAngle;
        body.localPosition = bodyBase + new Vector3(x, y, 0f);
        body.localRotation = bodyBaseRot * Quaternion.Euler(0f, yaw, 0f);

        if (head != null)
        {
            float mk = (t - mockStart) / mockDuration;
            if (mk >= 0f && mk < 1f)
            {
                float env = 1f - mk;
                float a = Mathf.Sin(mk * mockRate * 2f * Mathf.PI) * mockAngle * env;
                head.localRotation = headBaseRot * Quaternion.Euler(Mathf.Abs(a) * 0.5f, 0f, a);
            }
            else
            {
                head.localRotation = headBaseRot;
            }
        }

        if (flashUntil > 0f)
        {
            if (t >= flashUntil)
            {
                flashUntil = 0f;
                ApplyColor(baseColor);
            }
            else
            {
                ApplyColor(Color.Lerp(baseColor, flashColor, (flashUntil - t) / Mathf.Max(flashDuration, 0.001f)));
            }
        }
    }

    public void GuardBob() { guardBobStart = Time.time; }

    public void Mock() { mockStart = Time.time; }

    public void Flash(Color color, float duration)
    {
        flashColor = color;
        flashDuration = duration;
        flashUntil = Time.time + duration;
        ApplyColor(color);
    }

    private void ApplyColor(Color c)
    {
        if (mats == null) return;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            mats[i].color = c;
            mats[i].SetColor("_EmissionColor", new Color(c.r, c.g, c.b, 1f) * 0.8f);
        }
    }
}
