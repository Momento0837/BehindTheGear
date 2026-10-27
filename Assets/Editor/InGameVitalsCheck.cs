using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Checks the real prefab images without entering Play mode or saving scenes/assets.</summary>
public static class InGameVitalsCheck
{
    [MenuItem("Tools/Checks/In Game Vitals")]
    public static void Run()
    {
        const string result = "Temp/InGameVitalsValidation/result.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(result));
        GameObject root = null;
        try
        {
            if (Application.unityVersion != "6000.3.23f1") throw new Exception("Requires Unity 6000.3.23f1.");
            root = PrefabUtility.LoadPrefabContents("Assets/!UI/Ingame/InGame_Canvas.prefab");
            var hud = root.GetComponent<InGameVitalsHUD>();
            Require(hud != null, "Prefab HUD missing");
            CheckGauge(hud, "hp", root.transform.Find("HP_Frame/HP_Gage").GetComponent<Image>(), .5f);
            CheckGauge(hud, "mp", root.transform.Find("MP_Frame/MP_Gage").GetComponent<Image>(), .7f);
            File.WriteAllText(result, "PASS Unity " + Application.unityVersion + "\nHP 50%, MP 70%, immediate fill, gray trail 100->0 alpha and shrink, repeated losses, healing, duplicate geometry and draw order.\nNo scene or prefab saved.");
            Debug.Log("IN_GAME_VITALS_CHECK_PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(result, "FAIL\n" + error);
            Debug.LogException(error);
        }
        finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void CheckGauge(InGameVitalsHUD hud, string field, Image image, float ratio)
    {
        object gauge = typeof(InGameVitalsHUD).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
        var trail = (Image)gauge.GetType().GetField("trail").GetValue(gauge);
        Require(trail != null && trail.sprite == image.sprite, field + " sprite duplication");
        RectTransform a = image.rectTransform, b = trail.rectTransform;
        Require(a.parent == b.parent && b.GetSiblingIndex() + 1 == a.GetSiblingIndex(), field + " trail behind foreground");
        Require(a.anchoredPosition3D == b.anchoredPosition3D && a.sizeDelta == b.sizeDelta && a.anchorMin == b.anchorMin && a.anchorMax == b.anchorMax && a.pivot == b.pivot && a.localScale == b.localScale && a.localRotation == b.localRotation, field + " exact geometry");
        Require(trail.material.shader.name == "BehindTheGear/UI/GageTrail" && !ShaderUtil.ShaderHasError(trail.material.shader), field + " gray material");
        var set = gauge.GetType().GetMethod("SetValue");
        var tick = gauge.GetType().GetMethod("Tick");
        set.Invoke(gauge, new object[] { 1f });
        Near(trail.color.a, 0f, field + " no initial trail");
        set.Invoke(gauge, new object[] { ratio });
        Near(image.fillAmount, ratio, field + " immediate fill");
        Near(trail.fillAmount, 1f, field + " previous fill");
        Near(trail.color.a, 1f, field + " initial alpha");
        tick.Invoke(gauge, new object[] { .4f, .1f, .6f });
        Near(trail.fillAmount, (1f + ratio) * .5f, field + " halfway shrink");
        Near(trail.color.a, .5f, field + " halfway alpha");
        set.Invoke(gauge, new object[] { ratio * .5f });
        Require(trail.fillAmount >= ratio, field + " repeated loss retains visible trail");
        tick.Invoke(gauge, new object[] { 1f, .1f, .6f });
        Near(trail.fillAmount, image.fillAmount, field + " final fill");
        Near(trail.color.a, 0f, field + " final alpha");
        set.Invoke(gauge, new object[] { .1f });
        set.Invoke(gauge, new object[] { .9f });
        Near(image.fillAmount, .9f, field + " healing immediate");
        Near(trail.color.a, 0f, field + " healing clears trail");
    }

    private static void Near(float actual, float expected, string label) => Require(Mathf.Abs(actual - expected) < .001f, label);
    private static void Require(bool condition, string label) { if (!condition) throw new Exception(label); }
}
