using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CloakItemBindingCheck
{
    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        if (File.Exists("Temp/CheckCloakBinding.request")) EditorApplication.delayCall += Check;
    }

    [MenuItem("Tools/Checks/Cloak Item Binding")]
    private static void Check()
    {
        File.Delete("Temp/CheckCloakBinding.request");
        try
        {
            var item = Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
            if (item == null || item.ItemId != "Tattered_Cloak") throw new Exception("Garbage reward ID mismatch");
            var entry = InventoryItemCatalog.Find(item.ItemId);
            if (entry == null || item.DisplayName != entry.display.name || item.Description != entry.display.description)
                throw new Exception("Cloak JSON lookup mismatch");
            const string path = "Assets/!UI/Other/Tattered_Cloak.png";
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
            if (item.Icon != sprite || AssetDatabase.GetAssetPath(item.Icon) != path)
                throw new Exception("Cloak sprite reference does not resolve to imported PNG");
            string report = $"PASS: Garbage reward={item.ItemId}; name={item.DisplayName}; icon={item.Icon.name}; path={path}";
            File.WriteAllText("Temp/CloakItemBindingCheck.txt", report);
            Debug.Log(report);
        }
        catch (Exception error)
        {
            File.WriteAllText("Temp/CloakItemBindingCheck.txt", "FAIL: " + error);
            Debug.LogException(error);
        }
    }
}
