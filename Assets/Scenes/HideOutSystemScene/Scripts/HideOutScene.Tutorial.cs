using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    public sealed partial class HideOutScene
    {
        private TutorialButtonHighlight treatmentTutorialHint;

        private void UpdateTreatmentTutorialHint()
        {
            Button target = null;
            bool available = !editorPreview && player != null && player.Initialized && ui != null && ui.gameObject.activeInHierarchy
                && !EntryAnimationPlaying && !BaseArrivalTutorial.IsShowing && !SelenaBaseDialogue.IsShowing
                && !SceneFadeTransition.IsRunning && !trainingTransitionPending && !TreatmentRoomScene.IsActive
                && (cameraController == null || !cameraController.IsMoving);
            if (available && player.State.baseTutorialComplete)
            {
                Facility? next = !player.State.baseFirstTreatmentComplete ? Facility.Clinic
                    : player.State.baseTreatmentDialogueComplete && player.State.potionsReadyAt <= 0 ? Facility.Potions : null;
                if (next.HasValue)
                {
                    if (!hasSelection) facilityButtons.TryGetValue(next.Value, out target);
                    else if (selected == next.Value && upgradeOverlay != null && upgradeOverlay.activeInHierarchy)
                        target = useFacilityButton;
                }
            }
            if (target != null && treatmentTutorialHint == null) treatmentTutorialHint = gameObject.AddComponent<TutorialButtonHighlight>();
            if (treatmentTutorialHint != null) treatmentTutorialHint.SetTarget(target);
        }
    }
}
