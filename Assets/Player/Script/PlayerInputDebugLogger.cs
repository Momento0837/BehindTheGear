using UnityEngine;

/// <summary>Console-only input confirmation. Disable this component when input debugging is no longer needed.</summary>
[RequireComponent(typeof(PlayerInputReader))]
public sealed class PlayerInputDebugLogger : MonoBehaviour
{
    [SerializeField] private bool logInputEvents = true;

    private void Awake()
    {
        PlayerInputReader input = GetComponent<PlayerInputReader>();
        input.AttackZPressed += () => Log("Attack Z");
        input.AttackXPressed += () => Log("Attack X");
        input.AttackCPressed += () => Log("Attack C");
        input.AttackVPressed += () => Log("Attack V");
        input.PrimaryClickPressed += () => Log("Primary mouse click");
        input.SecondaryClickPressed += () => Log("Secondary mouse click");
    }

    private void Log(string inputName)
    {
        if (logInputEvents) Debug.Log($"[Player Input] {inputName} received.", this);
    }
}
