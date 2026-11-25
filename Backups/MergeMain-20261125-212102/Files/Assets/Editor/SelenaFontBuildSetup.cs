#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
[InitializeOnLoad]
public static class SelenaFontBuildSetup
{
    static SelenaFontBuildSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        const string request="Temp/Nov19/create-selena-font";
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request))return;
        File.Delete(request);
        try
        {
            if(Application.unityVersion!="6000.3.23f1")throw new Exception("Wrong Unity version");
            const string path="Assets/Resources/Dialogue/Fonts/SelenaDialogue.asset";
            if(File.Exists(path))throw new Exception("Will not overwrite existing font");
            var original=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Batang/Tangba11_Prop SDF.asset");
            var font=TMP_FontAsset.CreateFontAsset(original.sourceFontFile,(int)original.faceInfo.pointSize,4,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
            font.name="SelenaDialogue";
            font.fallbackFontAssetTable=new System.Collections.Generic.List<TMP_FontAsset>(original.fallbackFontAssetTable);
            font.material.name="SelenaDialogue Transparent Material";
            var serialized = new SerializedObject(font); serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue=false; serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(font,path);
            AssetDatabase.AddObjectToAsset(font.material,font);
            string letters="Selena셀레나";
            foreach(var json in Directory.GetFiles("Assets/Resources/Dialogue","*.json",SearchOption.AllDirectories))letters+=File.ReadAllText(json);
            font.TryAddCharacters(letters,out string missing);
            foreach(var atlas in font.atlasTextures)if(atlas!=null && !AssetDatabase.Contains(atlas))AssetDatabase.AddObjectToAsset(atlas,font);
            EditorUtility.SetDirty(font);AssetDatabase.SaveAssets();
            if(font.material.mainTexture!=font.atlasTexture || font.atlasPadding<4)throw new Exception("Invalid font atlas binding");
            File.WriteAllText("Temp/Nov19/selena-font-result.txt","PASS same source font; padding=4; transparent shader="+font.material.shader.name+"; glyphs="+font.characterTable.Count+"; atlas texture bound; atlas retained in build.");
        }
        catch(Exception e){File.WriteAllText("Temp/Nov19/selena-font-result.txt","FAIL "+e);Debug.LogException(e);}
    }
}
#endif