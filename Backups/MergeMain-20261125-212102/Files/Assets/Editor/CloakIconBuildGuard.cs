using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Stop a broken reference from silently becoming the generic gear icon in a build.
public sealed class CloakIconBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report)
    {
        var item = Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
        var expected = AssetDatabase.LoadAllAssetsAtPath("Assets/!UI/Other/Tattered_Cloak.png").OfType<Sprite>().FirstOrDefault();
        if (item == null || expected == null || item.Icon != expected)
            throw new BuildFailedException("TutorialGear icon must reference the imported Tattered_Cloak sprite. Repair the icon before building.");
    }
}
