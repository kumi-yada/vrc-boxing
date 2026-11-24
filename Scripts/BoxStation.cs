using UdonSharp;
using UnityEngine;

// Holds the world-space punch readout panel (written by PunchTarget) and the
// P-key visibility toggle.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BoxStation : UdonSharpBehaviour
{
    public GameObject debugPanelRoot;
    public bool debugPanelVisible = true;

    void Start()
    {
        if (debugPanelRoot != null) debugPanelRoot.SetActive(debugPanelVisible);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P)) ToggleDebugPanel();
    }

    public void ToggleDebugPanel()
    {
        if (debugPanelRoot != null) debugPanelRoot.SetActive(!debugPanelRoot.activeSelf);
    }
}
