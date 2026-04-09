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
    public TextMeshProUGUI statusText;
    public GameObject canvas;

    public float sideSwitchChance = 0.8f;

    [UdonSynced] public bool isInitialized = false;
    [UdonSynced] private bool isGameActive = false;
    [UdonSynced] private bool activeIsLeft = false;

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
