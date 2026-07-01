using UnityEngine;

// On-screen HUD that visualizes the live gesture-recognition state so thresholds
// can be tuned quickly at runtime. Drop this on any GameObject in the Hand
// Landmark Detection scene and assign the TankHandController.
//
// It reads only the read-only Debug* accessors on TankHandController and never
// changes behavior.
public class GestureDebugOverlay : MonoBehaviour
{
    [SerializeField] private TankHandController controller;
    [SerializeField] private bool showOverlay = true;
    [SerializeField] private Vector2 origin = new Vector2(16f, 16f);
    [SerializeField] private float width = 320f;

    private GUIStyle labelStyle;
    private Texture2D barBackground;
    private Texture2D barFill;

    private void EnsureResources()
    {
        if (barBackground == null)
        {
            barBackground = MakeTexture(new Color(0f, 0f, 0f, 0.55f));
        }
        if (barFill == null)
        {
            barFill = MakeTexture(new Color(0.25f, 0.85f, 0.45f, 0.9f));
        }
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };
        }
    }

    private static Texture2D MakeTexture(Color color)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private void OnGUI()
    {
        if (!showOverlay || controller == null)
        {
            return;
        }

        EnsureResources();

        float x = origin.x;
        float y = origin.y;
        float lineHeight = 22f;

        GUI.Label(
            new Rect(x, y, width, lineHeight),
            $"Mode: {controller.DebugCurrentMode}   Cooldown: {controller.DebugGestureCooldownRemaining:0.00}s",
            labelStyle);
        y += lineHeight;

        GUI.Label(
            new Rect(x, y, width, lineHeight),
            $"Buffer: {controller.DebugGestureFrameCount}/{controller.DebugGestureBufferSize}   " +
            $"Raw: {controller.DebugRawGestureIntent}",
            labelStyle);
        y += lineHeight;

        GUI.Label(
            new Rect(x, y, width, lineHeight),
            $"Dominant: {controller.DebugDominantGestureIntent} " +
            $"({controller.DebugDominantGestureRatio:0.00} / {controller.DebugDominantGestureConfidence:0.00})",
            labelStyle);
        y += lineHeight + 4f;

        y = DrawBar(x, y, "Pinch", controller.DebugPinchConfidence);
        y = DrawBar(x, y, "Fist", controller.DebugFistConfidence);
        y = DrawBar(x, y, "ThumbsUp", controller.DebugThumbsUpConfidence);
        y = DrawBar(x, y, "OpenHand", controller.DebugOpenHandConfidence);

        string navLabel = controller.DebugGroupNavigationDirection > 0
            ? "Nav >"
            : controller.DebugGroupNavigationDirection < 0 ? "Nav <" : "Nav";
        DrawBar(x, y, navLabel, controller.DebugGroupNavigationConfidence);
    }

    private float DrawBar(float x, float y, string label, float value)
    {
        const float labelWidth = 84f;
        const float barHeight = 16f;
        float barWidth = width - labelWidth;

        GUI.Label(new Rect(x, y, labelWidth, barHeight + 4f), label, labelStyle);

        Rect bg = new Rect(x + labelWidth, y, barWidth, barHeight);
        GUI.DrawTexture(bg, barBackground);

        Rect fill = new Rect(bg.x, bg.y, barWidth * Mathf.Clamp01(value), barHeight);
        GUI.DrawTexture(fill, barFill);

        GUI.Label(new Rect(bg.x + 6f, y, barWidth, barHeight + 4f), value.ToString("0.00"), labelStyle);

        return y + barHeight + 6f;
    }
}
