// GestureUIDisplay.cs
// On-screen HUD showing current gesture, mode, and legend.
// Attach to any GameObject and assign the GestureController in the Inspector.

using UnityEngine;

public class GestureUIDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GestureController gestureController;

    [Header("HUD Position")]
    [SerializeField] private float panelX = 10f;
    [SerializeField] private float panelY = 10f;
    [SerializeField] private float panelWidth = 280f;

    private GUIStyle _boxStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;

    private void OnGUI()
    {
        if (gestureController == null)
            return;

        InitStyles();

        string gesture = gestureController.currentGestureName;
        string mode = gestureController.currentMode.ToString();

        float rowH = 24f;
        float statusH = rowH * 3 + 12f;
        Rect statusRect = new Rect(panelX, panelY, panelWidth, statusH);

        GUI.Box(statusRect, GUIContent.none, _boxStyle);

        float y = panelY + 6f;
        GUI.Label(new Rect(panelX + 8, y, panelWidth - 16, rowH),
            "  <b>Gesture Tracking</b>", _titleStyle);
        y += rowH;

        GUI.Label(new Rect(panelX + 8, y, panelWidth - 16, rowH),
            $"Gesture:  <b>{gesture}</b>", _labelStyle);
        y += rowH;

        string modeColour = gestureController.currentMode == GestureController.InteractionMode.Explode
            ? "#ff8800" : "#00ccff";
        GUI.Label(new Rect(panelX + 8, y, panelWidth - 16, rowH),
            $"Mode:  <color={modeColour}><b>{mode}</b></color>", _labelStyle);

        string[] legend = new[]
        {
            "<b>Gesture Legend</b>",
            "Pinch + move wrist  ->  Rotate",
            "Pointing Up  ->  Zoom (wrist height)",
            "2-hand pinch + spread  ->  Zoom / Explode",
            "Closed Fist (hold)  ->  Toggle Zoom / Explode",
            "Open Palm (hold)  ->  Reload",
            "Thumb Up  ->  Next",
            "Thumb Down  ->  Previous",
            "Victory / ILoveYou  ->  Reset rotation",
        };

        float legendH = rowH * legend.Length + 12f;
        float legendY = panelY + statusH + 8f;
        Rect legendRect = new Rect(panelX, legendY, panelWidth, legendH);

        GUI.Box(legendRect, GUIContent.none, _boxStyle);

        float ly = legendY + 6f;
        foreach (string line in legend)
        {
            GUI.Label(new Rect(panelX + 8, ly, panelWidth - 16, rowH), line, _labelStyle);
            ly += rowH;
        }
    }

    private void InitStyles()
    {
        if (_boxStyle != null)
            return;

        var bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
        bgTex.Apply();

        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = bgTex }
        };

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            richText = true,
            normal = { textColor = Color.white }
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            richText = true,
            normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
        };
    }
}