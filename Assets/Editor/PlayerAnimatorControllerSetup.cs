using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Edits the imported controller through Unity's API so its transitions also appear in Animator.
[InitializeOnLoad]
internal static class PlayerAnimatorControllerSetup
{
    internal const string ControllerPath = "Assets/!Animation/Player/Player.controller";
    private static bool queued;

    static PlayerAnimatorControllerSetup() => Queue();

    internal static void Queue()
    {
        if (queued) return;
        queued = true;
        EditorApplication.delayCall += RepairWhenReady;
    }

    private static void RepairWhenReady()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            Queue();
            return;
        }

        Repair();
    }

    [MenuItem("Tools/Player/Repair Animator Controller")]
    private static void Repair()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0) return;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = FindState(machine, "Idle");
        AnimatorState running = FindState(machine, "Running");
        if (idle == null || running == null) return;

        bool changed = false;
        changed |= EnsureParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool);
        changed |= EnsureParameter(controller, "Attack", AnimatorControllerParameterType.Trigger);
        changed |= EnsureParameter(controller, "AttackIndex", AnimatorControllerParameterType.Int);

        for (int index = 1; index <= 3; index++)
        {
            AnimatorState standingAttack = FindState(machine, "Attack_" + index);
            AnimatorState runningAttack = FindState(machine, "Run_Attack_" + index);
            if (standingAttack == null || runningAttack == null) continue;

            changed |= EnsureAttackEntry(machine, standingAttack, index, false);
            changed |= EnsureAttackEntry(machine, runningAttack, index, true);
            changed |= EnsureAttackReturn(standingAttack, idle, false);
            changed |= EnsureAttackReturn(standingAttack, running, true);
            changed |= EnsureAttackReturn(runningAttack, idle, false);
            changed |= EnsureAttackReturn(runningAttack, running, true);
            if (changed)
            {
                EditorUtility.SetDirty(standingAttack);
                EditorUtility.SetDirty(runningAttack);
            }
        }

        if (!changed) return;
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
            if (child.state != null && child.state.name == name) return child.state;
        return null;
    }

    private static bool EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter parameter in controller.parameters)
            if (parameter.name == name && parameter.type == type) return false;
        controller.AddParameter(name, type);
        return true;
    }

    private static bool EnsureAttackEntry(AnimatorStateMachine machine, AnimatorState destination, int index, bool moving)
    {
        bool valid = false;
        bool changed = false;
        foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
        {
            if (transition.destinationState != destination) continue;
            if (!valid && IsAttackEntry(transition, index, moving))
            {
                valid = true;
                continue;
            }
            machine.RemoveAnyStateTransition(transition);
            changed = true;
        }
        if (valid) return changed;

        AnimatorStateTransition entry = machine.AddAnyStateTransition(destination);
        entry.hasExitTime = false;
        entry.duration = 0f;
        entry.canTransitionToSelf = false;
        entry.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        entry.AddCondition(AnimatorConditionMode.Equals, index, "AttackIndex");
        entry.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "IsMoving");
        return true;
    }

    private static bool IsAttackEntry(AnimatorStateTransition transition, int index, bool moving)
    {
        if (transition.hasExitTime || !Mathf.Approximately(transition.duration, 0f)
            || transition.conditions.Length != 3) return false;

        bool attack = false;
        bool attackIndex = false;
        bool isMoving = false;
        foreach (AnimatorCondition condition in transition.conditions)
        {
            if (condition.parameter == "Attack" && condition.mode == AnimatorConditionMode.If)
                attack = true;
            else if (condition.parameter == "AttackIndex" && condition.mode == AnimatorConditionMode.Equals
                && Mathf.Approximately(condition.threshold, index))
                attackIndex = true;
            else if (condition.parameter == "IsMoving"
                && condition.mode == (moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot))
                isMoving = true;
        }
        return attack && attackIndex && isMoving;
    }

    private static bool EnsureAttackReturn(AnimatorState source, AnimatorState destination, bool moving)
    {
        bool valid = false;
        bool changed = false;
        foreach (AnimatorStateTransition transition in source.transitions)
        {
            if (transition.destinationState != destination) continue;
            if (!valid && IsAttackReturn(transition, moving))
            {
                valid = true;
                continue;
            }
            source.RemoveTransition(transition);
            changed = true;
        }
        if (valid) return changed;

        AnimatorStateTransition exit = source.AddTransition(destination);
        exit.hasExitTime = true;
        exit.exitTime = 1f;
        exit.hasFixedDuration = true;
        exit.duration = .05f;
        exit.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "IsMoving");
        return true;
    }

    private static bool IsAttackReturn(AnimatorStateTransition transition, bool moving)
    {
        if (!transition.hasExitTime || transition.exitTime < 1f || transition.conditions.Length != 1) return false;
        AnimatorCondition condition = transition.conditions[0];
        return condition.parameter == "IsMoving"
            && condition.mode == (moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot);
    }
}

internal sealed class PlayerAnimatorControllerPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
        string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (Array.IndexOf(importedAssets, PlayerAnimatorControllerSetup.ControllerPath) >= 0)
            PlayerAnimatorControllerSetup.Queue();
    }
}
