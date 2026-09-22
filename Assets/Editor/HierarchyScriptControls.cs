using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds concise type badges and a one-click MonoBehaviour toggle to the right side of the Hierarchy.
/// This is editor-only and never included in a build.
/// </summary>
[InitializeOnLoad]
internal static class HierarchyScriptControls
{
    private const float ButtonWidth = 82f;
    private const float ActiveButtonWidth = 74f;

    static HierarchyScriptControls()
    {
        EditorApplication.hierarchyWindowItemOnGUI += DrawItemControls;
    }

    private static void DrawItemControls(int instanceId, Rect selectionRect)
    {
        if (EditorUtility.InstanceIDToObject(instanceId) is not GameObject gameObject)
            return;

        Rect rightArea = new(selectionRect.xMax - 250f, selectionRect.y, 245f, selectionRect.height);
        string badge = GetBadge(gameObject);
        if (!string.IsNullOrEmpty(badge))
        {
            Rect badgeRect = new(rightArea.x, rightArea.y, 58f, rightArea.height);
            GUI.Label(badgeRect, badge, EditorStyles.miniLabel);
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
        if (gameObject.TryGetComponent<Canvas>(out _)) return "[UI]";
        if (gameObject.TryGetComponent<Camera>(out _)) return "[CAM]";
        if (gameObject.TryGetComponent<Rigidbody2D>(out _) || gameObject.TryGetComponent<Collider2D>(out _)) return "[2D]";
        if (gameObject.TryGetComponent<Light>(out _)) return "[LIGHT]";
        return string.Empty;
    }
}
