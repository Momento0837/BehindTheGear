using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class FontCoverageCheck
{
    [MenuItem("Tools/Checks/Korean Font Coverage")]
    public static void Run()
    {
        const string output = "Temp/FontCoverageValidation/result.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        try
        {
            if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new Exception("Requires edit mode in Unity 6000.3.23f1");
            string titles = string.Join(" ", Resources.LoadAll<TextAsset>("Quests")
                .Select(data => JsonUtility.FromJson<QuestDefinition>(data.text).questName));
            Check("Assets/Fonts/Stardusts/PF스타더스트 ExtraBold.asset", titles + "...");
            Check("Assets/Fonts/Batang/Tangba11_Prop SDF.asset", "이동 점프 공격 상호작용 인벤토리 선택 시작 다음 닫기 취소 수락 거절");
            File.WriteAllText(output, "PASS Unity " + Application.unityVersion + "\nOriginal fonts, dynamic source references, writable atlas, multi-atlas; all stored quest names and Korean control/dialogue labels can generate glyphs. No assets saved.\n");
            Debug.Log("KOREAN_FONT_COVERAGE_PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(output, "FAIL\n" + error);
            Debug.LogException(error);
        }
    }

    private static void Check(string path, string text)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null || font.sourceFontFile == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic
            || !font.isMultiAtlasTexturesEnabled || !font.atlasTexture.isReadable)
            throw new Exception("Invalid dynamic font setup: " + path);
        if (!font.HasCharacters(text, out uint[] missing, false, true))
            throw new Exception("Missing glyphs in " + path + ": " + string.Join(",", missing.Select(code => "U+" + code.ToString("X4"))));
    }
}
