using TMPro;
using UdonSharp;
using UnityEngine;

// Marquee above the Holo-Coach: one icon card per punch of the called combo.
// Announce() flips every card in with a stagger so a re-announce after a break
// reads as a fresh call; SetStep() recolors cards as steps land.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ComboMarquee : UdonSharpBehaviour
{
    public const int CARD_PENDING = 0;
    public const int CARD_ACTIVE = 1;
    public const int CARD_DONE = 2;
    public const int CARD_BROKEN = 3;

    public TMP_Text title;
    public Transform[] cards;
    public Renderer[] cardFaces;
    public TMP_Text[] cardIcons;
    public TMP_Text[] cardLabels;

    [Header("Flip")]
    public float flipDuration = 0.25f;
    public float flipStagger = 0.15f;

    [Header("Card colors")]
    public Color pendingColor = new Color(0.15f, 0.15f, 0.2f, 1f);
    public Color activeColor = new Color(0.1f, 0.95f, 1f, 1f);
    public Color doneColor = new Color(0.2f, 1f, 0.35f, 1f);
    public Color brokenColor = new Color(1f, 0.15f, 0.15f, 1f);

    private Quaternion[] cardBaseRot;
    private float flipStart = -999f;
    private int shownCount;
    private Material[] faceMats;

    void Start()
    {
        int n = cards != null ? cards.Length : 0;
        cardBaseRot = new Quaternion[n];
        faceMats = new Material[n];
        for (int i = 0; i < n; i++)
        {
            if (cards[i] != null) cardBaseRot[i] = cards[i].localRotation;
            if (cardFaces != null && i < cardFaces.Length && cardFaces[i] != null)
                faceMats[i] = cardFaces[i].material;
            if (i >= shownCount && cards[i] != null) cards[i].gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (cards == null || cardBaseRot == null) return;
        float elapsed = Time.time - flipStart;
        if (elapsed > flipDuration + flipStagger * cards.Length + 0.1f) return;
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue;
            float k = Mathf.Clamp01((elapsed - flipStagger * i) / Mathf.Max(flipDuration, 0.001f));
            cards[i].localRotation = cardBaseRot[i] * Quaternion.Euler(0f, (1f - k) * 180f, 0f);
        }
    }

    public void Clear(string text)
    {
        shownCount = 0;
        if (title != null) title.text = text;
        if (cards == null) return;
        for (int i = 0; i < cards.Length; i++)
            if (cards[i] != null) cards[i].gameObject.SetActive(false);
    }

    public void SetTitle(string text)
    {
        if (title != null) title.text = text;
    }

    public void Announce(string text, int[] types, int count)
    {
        if (title != null) title.text = text;
        if (cards == null) return;
        shownCount = Mathf.Min(count, cards.Length);
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue;
            bool show = i < shownCount;
            cards[i].gameObject.SetActive(show);
            if (!show) continue;
            if (cardIcons != null && i < cardIcons.Length && cardIcons[i] != null)
                cardIcons[i].text = IconFor(types[i]);
            if (cardLabels != null && i < cardLabels.Length && cardLabels[i] != null)
                cardLabels[i].text = PunchClassifier.TypeName((PunchType)types[i]);
            SetCard(i, CARD_PENDING);
        }
        flipStart = Time.time;
    }

    public void SetStep(int activeStep)
    {
        for (int i = 0; i < shownCount; i++)
            SetCard(i, i < activeStep ? CARD_DONE : (i == activeStep ? CARD_ACTIVE : CARD_PENDING));
    }

    public void MarkBroken(int step)
    {
        if (step >= 0 && step < shownCount) SetCard(step, CARD_BROKEN);
    }

    public void MarkAllDone()
    {
        for (int i = 0; i < shownCount; i++) SetCard(i, CARD_DONE);
    }

    private void SetCard(int i, int cardState)
    {
        if (faceMats == null || i >= faceMats.Length || faceMats[i] == null) return;
        Color c = pendingColor;
        if (cardState == CARD_ACTIVE) c = activeColor;
        else if (cardState == CARD_DONE) c = doneColor;
        else if (cardState == CARD_BROKEN) c = brokenColor;
        faceMats[i].color = c;
        faceMats[i].SetColor("_EmissionColor", c * (cardState == CARD_PENDING ? 0.2f : 1.2f));
    }

    // Placeholder glyphs until the icon art lands; LiberationSans has no arrows.
    private string IconFor(int type)
    {
        if (type == (int)PunchType.Jab) return "J";
        if (type == (int)PunchType.Hook) return "H";
        if (type == (int)PunchType.Uppercut) return "U";
        return "?";
    }
}
