using System.IO;
using UnityEditor;
using UnityEngine;

namespace BehindTheGear.Tools.MapPoints
{
    /// <summary>팀원 배포용: 툴 폴더 전체를 .unitypackage 한 파일로 내보낸다.</summary>
    internal static class MapPointToolPackageExporter
    {
        const string AsmdefName = "BehindTheGear.MapPointTool.Editor";

        [MenuItem("Tools/BehindTheGear/Map Point Tool/팀원 배포용 .unitypackage 만들기", priority = 120)]
        static void ExportUnityPackage()
        {
            var root = FindToolRoot();
            if (root == null)
            {
                EditorUtility.DisplayDialog("Map Point Tool", "툴 폴더를 찾지 못했습니다(" + AsmdefName + ".asmdef).", "확인");
                return;
            }

            var target = EditorUtility.SaveFilePanel("Map Point Tool 패키지 저장", string.Empty, "MapPointTool.unitypackage", "unitypackage");
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            AssetDatabase.ExportPackage(root, target, ExportPackageOptions.Recurse);
            Debug.Log("[MapPointTool] 패키지를 만들었습니다: " + target);
            EditorUtility.RevealInFinder(target);
        }

        /// <summary>asmdef 위치(…/MapPointTool/Editor)의 상위 폴더를 툴 루트로 본다. 폴더를 옮겨도 동작한다.</summary>
        static string FindToolRoot()
        {
            foreach (var guid in AssetDatabase.FindAssets(AsmdefName + " t:AssemblyDefinitionAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != AsmdefName)
                {
                    continue;
                }

                var editorFolder = Path.GetDirectoryName(path);
                var root = Path.GetDirectoryName(editorFolder);
                return string.IsNullOrEmpty(root) ? null : root.Replace('\\', '/');
            }

            return null;
        }
    }
}
