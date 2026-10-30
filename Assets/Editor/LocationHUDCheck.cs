using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class LocationHUDCheck
{
    [MenuItem("Tools/Checks/Location Notice")]
    public static void Run()
    {
        const string result = "Temp/LocationHUDValidation/result.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(result));
        LocationHUD hud = null;
        try
        {
            Require(Application.unityVersion == "6000.3.23f1" && !EditorApplication.isPlayingOrWillChangePlaymode, "Requires edit mode in Unity 6000.3.23f1");
            hud = LocationHUD.Create();
            var group = hud.GetComponent<CanvasGroup>();
            var text = hud.GetComponentInChildren<TMP_Text>();
            Require(group.alpha == 0f && !group.blocksRaycasts, "Initially hidden and non-blocking");
            foreach (string name in new[] { "포레스트", "가베지", "마켓" })
            {
                hud.SetLocation(name);
                Require(hud.CurrentLocation == name && text.font.HasCharacters(name), "Korean location name");
                Tick(hud, .2f);
                Require(group.alpha > .4f && group.alpha < .6f, "Smooth fade in midpoint");
                Tick(hud, .2f);
                Require(group.alpha == 1f, "Fully visible");
                Tick(hud, 2.5f);
                Tick(hud, .3f);
                Require(group.alpha > .4f && group.alpha < .6f, "Smooth fade out midpoint");
                Tick(hud, .4f);
                Require(group.alpha == 0f, "Fully hidden after notice");
            }
            hud.SetLocation("가베지");
            Tick(hud, .2f);
            hud.SetLocation("마켓");
            Require(group.alpha == 0f, "New arrival restarts the notice");
            var panel = (RectTransform)text.transform.parent;
            Require(panel.anchorMin == new Vector2(.5f, 1f) && panel.anchorMax == panel.anchorMin, "Top center placement");
            File.WriteAllText(result, "PASS Unity " + Application.unityVersion + "\nThree Korean location labels, top center placement, smooth fade-in/hold/fade-out, repeat arrival, non-blocking overlay. No scene saved.\n");
            Debug.Log("LOCATION_HUD_CHECK_PASS");
        }
        catch (Exception exception)
        {
            File.WriteAllText(result, "FAIL\n" + exception);
            Debug.LogException(exception);
        }
        finally { if (hud != null) UnityEngine.Object.DestroyImmediate(hud.gameObject); }
    }

    private static void Tick(LocationHUD hud, float delta) => typeof(LocationHUD).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, new object[] { delta });
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
