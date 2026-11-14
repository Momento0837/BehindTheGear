using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Field context captured when F is used on the hideout entrance, not inside the base.</summary>
[Serializable]
public sealed class FieldReturnState
{
    public string scenePath;
    public Vector3 position;
    public float facing = 1f;
    public bool hasBounds;
    public Vector2 bounds;
    public string questName, objective;
    public bool showObjective;
    public StoryFieldState story;

    public static FieldReturnState Capture(GameObject player, Scene scene)
    {
        var limit = player.GetComponent<PlayerMapBounds2D>();
        var flow = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<StorySceneFlow>(true)).FirstOrDefault();
        return new FieldReturnState
        {
            scenePath = scene.path, position = player.transform.position,
            facing = player.GetComponent<PlayerSpriteFacing>()?.FacingDirection ?? 1f,
            hasBounds = limit != null && limit.HasHorizontalBounds,
            bounds = limit != null ? limit.HorizontalBounds : Vector2.zero,
            questName = QuestTracker.CurrentQuestName, objective = QuestTracker.CurrentObjective,
            showObjective = QuestTracker.ShowObjective, story = flow != null ? flow.CaptureFieldState() : null
        };
    }

    public void RestorePlacement(GameObject player, Scene scene)
    {
        var limit = player.GetComponent<PlayerMapBounds2D>();
        if (limit != null) limit.ClearHorizontalBounds();
        player.transform.position = position;
        var body = player.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = position; body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
        Physics2D.SyncTransforms();
        if (hasBounds)
        {
            if (limit == null) limit = player.AddComponent<PlayerMapBounds2D>();
            limit.SetHorizontalBounds(bounds);
            foreach (var camera in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerCameraFollow2D>(true)))
                camera.SetStoryBounds(bounds);
        }
        player.GetComponent<PlayerSpriteFacing>()?.FaceAttack(facing);
    }

    public void RestoreQuest(object owner)
    {
        if (string.IsNullOrEmpty(questName)) QuestTracker.Reset();
        else QuestTracker.Restore(owner, questName, objective, showObjective);
    }

    public void Validate()
    {
        bool finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        if ((scenePath != "Assets/Scenes/Main/Forest.unity" && scenePath != "Assets/Scenes/Main/Market.unity")
            || !finite(position.x) || !finite(position.y) || !finite(position.z) || !finite(facing)
            || (hasBounds && (!finite(bounds.x) || !finite(bounds.y) || bounds.x >= bounds.y))
            || (story != null && (story.chapter < 0 || story.chapter > 2)))
            throw new InvalidDataException("기지 복귀 위치 또는 스토리 기록이 올바르지 않습니다.");
    }
}

[Serializable]
public sealed class StoryFieldState
{
    public int chapter;
    public string guide;
    public bool marketMap, ready, marketNpcSpoken, greetingCompleted, garbageCollected;
    public bool hasSelena;
    public Vector3 selenaPosition;
    public Vector3 selenaScale = Vector3.one;
    public FieldMonsterState[] monsters;
}

[Serializable]
public sealed class FieldMonsterState
{
    public Vector3 position;
    public int health;
}
