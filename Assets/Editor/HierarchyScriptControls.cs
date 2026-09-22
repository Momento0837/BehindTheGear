using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds concise type badges and a one-click MonoBehaviour toggle to the right side of the Hierarchy.
/// This is editor-only and never included in a build.
/// </summary>
[InitializeOnLoad]
internal static class HierarchyScriptControls
{
    private const float ButtonWidth = 82f;
    private const float ActiveButtonWidth = 74f;
    private static bool refreshingIcons;
    private static readonly Texture2D[] SemanticIcons = new Texture2D[5];

    static HierarchyScriptControls()
    {
        EditorApplication.hierarchyWindowItemOnGUI += DrawItemControls;
        EditorApplication.hierarchyChanged += RefreshObjectIcons;
        EditorApplication.delayCall += RefreshObjectIcons;
    }

    private static void DrawItemControls(int instanceId, Rect selectionRect)
    {
        if (EditorUtility.InstanceIDToObject(instanceId) is not GameObject gameObject)
            return;

        Rect rightArea = new(selectionRect.xMax - 310f, selectionRect.y, 305f, selectionRect.height);
        string badge = GetBadge(gameObject);
        if (!string.IsNullOrEmpty(badge))
        {
            Rect badgeRect = new(rightArea.x, rightArea.y, 116f, rightArea.height);
            Color badgePreviousColor = GUI.color;
            GUI.color = GetLabelColor(gameObject);
            GUI.Label(badgeRect, badge, EditorStyles.miniBoldLabel);
            GUI.color = badgePreviousColor;
        }

        Rect activeRect = new(selectionRect.xMax - ButtonWidth - ActiveButtonWidth - 4f, selectionRect.y + 1f, ActiveButtonWidth, selectionRect.height - 2f);
        bool active = gameObject.activeSelf;
        GUIContent activeContent = new(active ? "Object: ON" : "Object: OFF", "Click to enable or disable this GameObject.");
        Color previousColor = GUI.color;
        GUI.color = active ? new Color(.65f, .85f, 1f) : new Color(1f, .62f, .62f);
        if (GUI.Button(activeRect, activeContent, EditorStyles.miniButton))
        {
            Undo.RegisterFullObjectHierarchyUndo(gameObject, $"Toggle {gameObject.name}");
            gameObject.SetActive(!active);
        }
        GUI.color = previousColor;

        MonoBehaviour[] scripts = GetUserScripts(gameObject);
        if (scripts.Length == 0)
            return;

        bool allEnabled = scripts.All(script => script.enabled);
        bool anyEnabled = scripts.Any(script => script.enabled);
        Rect toggleRect = new(selectionRect.xMax - ButtonWidth, selectionRect.y + 1f, ButtonWidth, selectionRect.height - 2f);
        GUIContent content = new(allEnabled ? "Scripts: ON" : anyEnabled ? "Scripts: MIX" : "Scripts: OFF",
            "Click to turn every script on this GameObject on or off.");

        GUI.color = allEnabled ? new Color(.65f, 1f, .65f) : anyEnabled ? new Color(1f, .88f, .45f) : new Color(1f, .62f, .62f);
        if (GUI.Button(toggleRect, content, EditorStyles.miniButton))
        {
            bool nextState = !allEnabled;
            Undo.RecordObjects(scripts, $"Toggle scripts on {gameObject.name}");
            foreach (MonoBehaviour script in scripts)
            {
                script.enabled = nextState;
                EditorUtility.SetDirty(script);
            }
        }
        GUI.color = previousColor;
    }

    private static MonoBehaviour[] GetUserScripts(GameObject gameObject)
    {
        return gameObject.GetComponents<MonoBehaviour>()
            .Where(script => script != null)
            .Where(script =>
            {
                MonoScript monoScript = MonoScript.FromMonoBehaviour(script);
                string path = monoScript == null ? string.Empty : AssetDatabase.GetAssetPath(monoScript);
                return path.StartsWith("Assets/") && !path.Contains("/Editor/");
            })
            .ToArray();
    }

    private static string GetBadge(GameObject gameObject)
    {
        if (gameObject.GetComponent<PlayerMovement2D>() != null) return "PLAYER";
        if (gameObject.TryGetComponent<Canvas>(out _)) return "UI";
        if (gameObject.TryGetComponent<PlatformEffector2D>(out _)) return "ONE-WAY";
        if (gameObject.TryGetComponent<Camera>(out _)) return "CAMERA";
        if (gameObject.TryGetComponent<Rigidbody2D>(out _) || gameObject.TryGetComponent<Collider2D>(out _)) return "2D PHYSICS";
        if (gameObject.TryGetComponent<Light>(out _)) return "LIGHT";
        return string.Empty;
    }

    private static Color GetLabelColor(GameObject gameObject)
    {
        if (gameObject.GetComponent<PlayerMovement2D>() != null) return new Color(.18f, .8f, 1f, .9f);
        if (gameObject.TryGetComponent<Canvas>(out _)) return new Color(.75f, .4f, 1f, .9f);
        if (gameObject.TryGetComponent<PlatformEffector2D>(out _)) return new Color(1f, .65f, .2f, .9f);
        if (gameObject.TryGetComponent<Camera>(out _)) return new Color(.5f, .85f, 1f, .9f);
        return new Color(.55f, .55f, .55f, .55f);
    }

    private static void RefreshObjectIcons()
    {
        if (refreshingIcons) return;
        refreshingIcons = true;
        try
        {
            foreach (GameObject gameObject in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Texture2D icon = GetSemanticIcon(gameObject);
                if (icon != null)
                    EditorGUIUtility.SetIconForObject(gameObject, icon);
            }
        }
        finally { refreshingIcons = false; }
    }

    private static Texture2D GetSemanticIcon(GameObject gameObject)
    {
        if (gameObject.GetComponent<PlayerMovement2D>() != null) return GetOrCreateIcon(0);
        if (gameObject.TryGetComponent<Canvas>(out _)) return GetOrCreateIcon(1);
        if (gameObject.TryGetComponent<PlatformEffector2D>(out _)) return GetOrCreateIcon(2);
        if (gameObject.TryGetComponent<Camera>(out _)) return GetOrCreateIcon(3);
        if (gameObject.TryGetComponent<Rigidbody2D>(out _) || gameObject.TryGetComponent<Collider2D>(out _)) return GetOrCreateIcon(4);
        return null;
    }

    private static Texture2D GetOrCreateIcon(int kind)
    {
        if (SemanticIcons[kind] != null) return SemanticIcons[kind];

        Texture2D icon = new(16, 16, TextureFormat.RGBA32, false)
        {
            name = "BehindTheGear Hierarchy Icon",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
        };
        Color clear = new(0f, 0f, 0f, 0f);
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++) icon.SetPixel(x, y, clear);

        Color color = kind switch
        {
            0 => new Color(.18f, .8f, 1f),       // Player
            1 => new Color(.75f, .4f, 1f),       // UI
            2 => new Color(1f, .65f, .2f),       // One-way platform
            3 => new Color(.5f, .85f, 1f),       // Camera
            _ => new Color(.4f, 1f, .55f),       // 2D physics
        };

        switch (kind)
        {
            case 0: // Head and body
                FillCircle(icon, 8, 11, 3, color);
                FillRect(icon, 5, 2, 6, 7, color);
                break;
            case 1: // UI window frame
                FillRect(icon, 1, 2, 14, 12, color);
                FillRect(icon, 3, 4, 10, 8, clear);
                FillRect(icon, 1, 12, 14, 2, color);
                break;
            case 2: // Platform with down arrow
                FillRect(icon, 1, 3, 14, 3, color);
                FillRect(icon, 7, 7, 2, 5, color);
                FillRect(icon, 5, 9, 6, 2, color);
                break;
            case 3: // Camera body and lens
                FillRect(icon, 1, 4, 11, 8, color);
                FillRect(icon, 12, 6, 3, 4, color);
                FillCircle(icon, 6, 8, 3, clear);
                break;
            default: // Physics circle / body
                FillCircle(icon, 8, 8, 6, color);
                FillCircle(icon, 8, 8, 3, clear);
                break;
        }

        icon.Apply();
        SemanticIcons[kind] = icon;
        return icon;
    }

    private static void FillRect(Texture2D texture, int x, int y, int width, int height, Color color)
    {
        for (int px = x; px < x + width; px++)
            for (int py = y; py < y + height; py++)
                if (px >= 0 && px < 16 && py >= 0 && py < 16) texture.SetPixel(px, py, color);
    }

    private static void FillCircle(Texture2D texture, int centerX, int centerY, int radius, Color color)
    {
        int radiusSquared = radius * radius;
        for (int x = centerX - radius; x <= centerX + radius; x++)
            for (int y = centerY - radius; y <= centerY + radius; y++)
                if (x >= 0 && x < 16 && y >= 0 && y < 16 && (x - centerX) * (x - centerX) + (y - centerY) * (y - centerY) <= radiusSquared)
                    texture.SetPixel(x, y, color);
    }
}
