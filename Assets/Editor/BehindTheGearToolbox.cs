using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A compact project-local toolbox for favorites, scene bookmarks, selection metadata, and player tuning.</summary>
internal sealed class BehindTheGearToolbox : EditorWindow
{
    private const string FavoritesKey = "BehindTheGear.Toolbox.Favorites";
    private const string BookmarksKey = "BehindTheGear.Toolbox.Bookmarks";

    [Serializable] private sealed class StringStore { public List<string> values = new(); }
    [Serializable] private sealed class BookmarkStore { public List<SceneBookmark> values = new(); }
    [Serializable] private sealed class SceneBookmark
    {
        public string name;
        public string scenePath;
        public Vector3 pivot;
        public Quaternion rotation;
        public float size;
    }

    private Vector2 scroll;
    private string bookmarkName = "New Bookmark";
    private StringStore favorites;
    private BookmarkStore bookmarks;

    [MenuItem("Behind The Gear/Toolbox")]
    private static void Open() => GetWindow<BehindTheGearToolbox>("Behind The Gear");

    private void OnEnable()
    {
        favorites = Load<StringStore>(FavoritesKey) ?? new StringStore();
        bookmarks = Load<BookmarkStore>(BookmarksKey) ?? new BookmarkStore();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawSelectionTools();
        EditorGUILayout.Space(8);
        DrawFavorites();
        EditorGUILayout.Space(8);
        DrawBookmarks();
        EditorGUILayout.Space(8);
        DrawPlayerTestPanel();
        EditorGUILayout.EndScrollView();
    }

    private void DrawSelectionTools()
    {
        EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
        GameObject[] selected = Selection.gameObjects;
        if (selected.Length == 0)
        {
            EditorGUILayout.HelpBox("Select one or more GameObjects to edit their Layer, Tag, and active state.", MessageType.Info);
            return;
        }

        GameObject first = selected[0];
        EditorGUILayout.LabelField($"{selected.Length} selected · Components: {first.GetComponents<Component>().Length}");
        EditorGUI.BeginChangeCheck();
        int layer = EditorGUILayout.LayerField("Layer", first.layer);
        string tag = EditorGUILayout.TagField("Tag", first.tag);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObjects(selected, "Change Layer or Tag");
            foreach (GameObject gameObject in selected)
            {
                gameObject.layer = layer;
                gameObject.tag = tag;
                EditorUtility.SetDirty(gameObject);
            }
        }

        bool allActive = selected.All(item => item.activeSelf);
        if (GUILayout.Button(allActive ? "Disable Selected Objects" : "Enable Selected Objects"))
        {
            foreach (GameObject gameObject in selected)
                Undo.RegisterFullObjectHierarchyUndo(gameObject, "Toggle Selected Objects");
            foreach (GameObject gameObject in selected) gameObject.SetActive(!allActive);
        }
    }

    private void DrawFavorites()
    {
        EditorGUILayout.LabelField("Object Favorites", EditorStyles.boldLabel);
        if (GUILayout.Button("Add Current Selection"))
        {
            foreach (GameObject item in Selection.gameObjects)
            {
                string id = GlobalObjectId.GetGlobalObjectIdSlow(item).ToString();
                if (!favorites.values.Contains(id)) favorites.values.Add(id);
            }
            Save(FavoritesKey, favorites);
        }

        for (int i = favorites.values.Count - 1; i >= 0; i--)
        {
            UnityEngine.Object target = Resolve(favorites.values[i]);
            EditorGUILayout.BeginHorizontal();
            if (target != null && GUILayout.Button(target.name, EditorStyles.linkLabel))
                Selection.activeObject = target;
            else
                EditorGUILayout.LabelField("Missing object", EditorStyles.miniLabel);
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                favorites.values.RemoveAt(i);
                Save(FavoritesKey, favorites);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawBookmarks()
    {
        EditorGUILayout.LabelField("Scene Bookmarks", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        bookmarkName = EditorGUILayout.TextField(bookmarkName);
        if (GUILayout.Button("Save", GUILayout.Width(56f)))
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            bookmarks.values.Add(new SceneBookmark
            {
                name = string.IsNullOrWhiteSpace(bookmarkName) ? "Bookmark" : bookmarkName,
                scenePath = SceneManager.GetActiveScene().path,
                pivot = view.pivot,
                rotation = view.rotation,
                size = view.size,
            });
            Save(BookmarksKey, bookmarks);
        }
        EditorGUILayout.EndHorizontal();

        for (int i = bookmarks.values.Count - 1; i >= 0; i--)
        {
            SceneBookmark bookmark = bookmarks.values[i];
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(bookmark.name, EditorStyles.miniButtonLeft))
            {
                if (bookmark.scenePath == SceneManager.GetActiveScene().path && SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.LookAt(bookmark.pivot, bookmark.rotation, bookmark.size, false, true);
                else
                    EditorUtility.DisplayDialog("Different Scene", "Open the bookmark's scene first, then use this bookmark.", "OK");
            }
            if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(24f)))
            {
                bookmarks.values.RemoveAt(i);
                Save(BookmarksKey, bookmarks);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawPlayerTestPanel()
    {
        EditorGUILayout.LabelField("Player Test Panel", EditorStyles.boldLabel);
        PlayerMovement2D player = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<PlayerMovement2D>()
            : null;
        player ??= FindFirstObjectByType<PlayerMovement2D>();
        if (player == null)
        {
            EditorGUILayout.HelpBox("No PlayerMovement2D was found in the open scene.", MessageType.Info);
            return;
        }

        SerializedObject serializedPlayer = new(player);
        serializedPlayer.Update();
        EditorGUILayout.ObjectField("Player", player, typeof(PlayerMovement2D), true);
        EditorGUILayout.PropertyField(serializedPlayer.FindProperty("moveSpeed"), new GUIContent("Move Speed"));
        EditorGUILayout.PropertyField(serializedPlayer.FindProperty("jumpVelocity"), new GUIContent("Jump Velocity"));
        EditorGUILayout.PropertyField(serializedPlayer.FindProperty("crouchSpeedMultiplier"), new GUIContent("Crouch Speed Multiplier"));
        if (serializedPlayer.ApplyModifiedProperties()) EditorUtility.SetDirty(player);
    }

    private static T Load<T>(string key) where T : class
    {
        string json = EditorPrefs.GetString(key, string.Empty);
        return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<T>(json);
    }

    private static void Save(string key, object value) => EditorPrefs.SetString(key, JsonUtility.ToJson(value));

    private static UnityEngine.Object Resolve(string serializedId)
    {
        return GlobalObjectId.TryParse(serializedId, out GlobalObjectId id)
            ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id)
            : null;
    }
}
