
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BoxStation : UdonSharpBehaviour
{
    public BoxPad leftPad;
    public BoxPad rightPad;

    [UdonSynced] private bool isGameActive = false;
    [UdonSynced] private bool activeIsLeft = false;
    
    private PadPosition[] allPositions = new PadPosition[] { PadPosition.Jab, PadPosition.Cross, PadPosition.Hook };

    void Start()
    {
        leftPad.InitializePad(PadPosition.Jab, true);
        rightPad.InitializePad(PadPosition.Jab, false);
    }

    public void StartGame(VRCPlayerApi player)
    {
        if (isGameActive)
            return;

        Debug.Log("Starting game with player: " + player.displayName);
        Networking.SetOwner(player, gameObject);
        isGameActive = true;

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

        if (activeIsLeft != isLeft)
        {
            Debug.Log("Hit on wrong pad. Force: " + force);
            return;
        }

        Debug.Log((isLeft ? "Left" : "Right") + " pad hit with force: " + force);
        ShowPad(activeIsLeft);
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
}
