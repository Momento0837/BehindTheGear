using UnityEngine;

/// <summary>
/// Optional settings for the account cloud save. The asset is not required:
/// without Resources/CloudSaveConfig the defaults below are used and the project ID
/// comes from Project Settings > Services (Application.cloudProjectId).
/// </summary>
[CreateAssetMenu(menuName = "BehindTheGear/Cloud Save Config", fileName = "CloudSaveConfig")]
public sealed class CloudSaveConfig : ScriptableObject
{
    [Tooltip("비워두면 Project Settings > Services에 연결된 Unity Cloud 프로젝트 ID를 사용합니다.")]
    public string projectIdOverride = "";
    [Tooltip("Cloud Save 플레이어 데이터 키. 영문/숫자/_/- 만 사용합니다. 바꾸면 기존 계정 저장과 분리됩니다.")]
    public string slotKey = "manual_slot_1";
    [Min(5)] public int requestTimeoutSeconds = 15;
    [Tooltip("메인 메뉴에서 저장된 로그인 세션으로 자동 로그인 후 동기화합니다.")]
    public bool autoSyncOnMenu = true;

    private static CloudSaveConfig current;
    public static CloudSaveConfig Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<CloudSaveConfig>("CloudSaveConfig");
            if (current == null)
            {
                current = CreateInstance<CloudSaveConfig>();
                current.hideFlags = HideFlags.HideAndDontSave;
            }
            return current;
        }
    }

    public static string ProjectId
    {
        get
        {
            string overrideId = Current.projectIdOverride != null ? Current.projectIdOverride.Trim() : "";
            return overrideId.Length > 0 ? overrideId : Application.cloudProjectId;
        }
    }

    public static string SlotKey
    {
        get
        {
            string key = Current.slotKey;
            if (string.IsNullOrEmpty(key) || key.Length > 255) return "manual_slot_1";
            foreach (char c in key)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-'))
                    return "manual_slot_1";
            return key;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => current = null;
}
