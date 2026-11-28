using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BehindTheGear.Tools.MapPoints
{
    public enum MapExportMode
    {
        /// <summary>포인트 Empty Object만 담은 프리팹. 루트는 (0,0,0), 포인트는 절대 좌표.</summary>
        PointsOnly = 0,

        /// <summary>맵 프리팹을 복제하고 그 안에 포인트를 추가한 프리팹. 원본 맵은 그대로 둔다.</summary>
        MapWithPoints = 1,
    }

    internal struct MapPointVersion
    {
        public int version;
        public string assetPath;
    }

    /// <summary>
    /// 포인트를 프리팹으로 내보내고, 이전에 내보낸 버전을 다시 읽어 온다.
    ///
    /// 내보낸 프리팹 구조:
    /// <code>
    /// [Root]                       PointsOnly: 빈 루트(0,0,0) / MapWithPoints: 복제한 맵 루트
    ///   MapPoints
    ///     PlayerSpawns  → PlayerSpawn_01 …
    ///     MonsterSpawns → MonsterSpawn_01 …
    ///     Portals       → Portal_01 …
    ///     MiniBosses    → MiniBoss_01 …
    /// </code>
    /// 모든 포인트는 Transform만 가진 Empty GameObject다(런타임 스크립트 의존 없음).
    /// </summary>
    internal static class MapPointExporter
    {
        public const string ContainerName = "MapPoints";

        public static List<MapPointVersion> FindExistingVersions(string folder, string baseName)
        {
            var result = new List<MapPointVersion>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return result;
            }

            foreach (var file in Directory.GetFiles(folder, "*.prefab", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (MapPointMath.TryParseVersion(name, baseName, out var version))
                {
                    result.Add(new MapPointVersion
                    {
                        version = version,
                        assetPath = (folder + "/" + Path.GetFileName(file)).Replace('\\', '/'),
                    });
                }
            }

            result.Sort((a, b) => a.version.CompareTo(b.version));
            return result;
        }

        public static string GetNextOutputPath(string folder, string baseName, out int version)
        {
            var existing = FindExistingVersions(folder, baseName).Select(v => Path.GetFileNameWithoutExtension(v.assetPath));
            version = MapPointMath.GetNextVersion(baseName, existing);

            // 같은 이름 파일이 (대소문자 차이 등으로) 이미 있으면 다음 번호로 넘어간다. 기존 파일은 절대 덮어쓰지 않는다.
            var path = folder + "/" + MapPointMath.BuildFileName(baseName, version) + ".prefab";
            while (File.Exists(path))
            {
                version++;
                path = folder + "/" + MapPointMath.BuildFileName(baseName, version) + ".prefab";
            }

            return path;
        }

        public static bool Export(
            string sourceMapPath,
            Vector3 mapOrigin,
            IList<MapPointData> points,
            float alignedZ,
            MapExportMode mode,
            out string outputPath,
            out string error)
        {
            outputPath = null;
            error = null;

            if (string.IsNullOrEmpty(sourceMapPath) || AssetDatabase.LoadAssetAtPath<GameObject>(sourceMapPath) == null)
            {
                error = "원본 맵 프리팹을 찾을 수 없습니다.";
                return false;
            }

            if (points == null || points.Count == 0)
            {
                error = "내보낼 포인트가 없습니다.";
                return false;
            }

            var folder = (Path.GetDirectoryName(sourceMapPath) ?? "Assets").Replace('\\', '/');
            var baseName = Path.GetFileNameWithoutExtension(sourceMapPath);
            var path = GetNextOutputPath(folder, baseName, out _);

            var success = mode == MapExportMode.PointsOnly
                ? ExportPointsOnly(path, mapOrigin, points, alignedZ, out error)
                : ExportMapWithPoints(sourceMapPath, path, points, alignedZ, out error);

            if (!success)
            {
                // 이번 호출에서 새로 만든 파일만 정리한다(기존 파일·원본 맵은 건드리지 않음).
                if (File.Exists(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }

                return false;
            }

            outputPath = path;
            return true;
        }

        static bool ExportPointsOnly(string path, Vector3 mapOrigin, IList<MapPointData> points, float alignedZ, out string error)
        {
            error = null;

            // 사용자의 현재 씬을 더럽히지 않도록 임시 Preview Scene에서 조립한다.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(Path.GetFileNameWithoutExtension(path));
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = Vector3.zero;

                BuildContainer(root.transform, points, mapOrigin, alignedZ);

                PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
                if (!saved)
                {
                    error = "프리팹 저장에 실패했습니다: " + path;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static bool ExportMapWithPoints(string sourceMapPath, string path, IList<MapPointData> points, float alignedZ, out string error)
        {
            error = null;

            // 원본을 복제한 새 에셋(새 GUID)에만 포인트를 추가한다. 원본 맵 프리팹은 수정하지 않는다.
            if (!AssetDatabase.CopyAsset(sourceMapPath, path))
            {
                error = "맵 프리팹 복제에 실패했습니다: " + path;
                return false;
            }

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);

                // 이미 포인트가 들어 있던 프리팹을 복제한 경우 중복되지 않게 이전 컨테이너를 제거한다.
                for (var i = contents.transform.childCount - 1; i >= 0; i--)
                {
                    var child = contents.transform.GetChild(i);
                    if (child.name == ContainerName)
                    {
                        Object.DestroyImmediate(child.gameObject);
                    }
                }

                BuildContainer(contents.transform, points, contents.transform.position, alignedZ);

                PrefabUtility.SaveAsPrefabAsset(contents, path, out var saved);
                if (!saved)
                {
                    error = "프리팹 저장에 실패했습니다: " + path;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                if (contents != null)
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        static void BuildContainer(Transform parent, IList<MapPointData> points, Vector3 mapOrigin, float alignedZ)
        {
            var container = new GameObject(ContainerName).transform;
            container.SetParent(parent, false);

            foreach (var type in MapPointTypeInfo.All)
            {
                var group = new GameObject(MapPointTypeInfo.GroupName(type)).transform;
                group.SetParent(container, false);

                foreach (var point in points)
                {
                    if (point.type != type)
                    {
                        continue;
                    }

                    var go = new GameObject(string.IsNullOrWhiteSpace(point.name) ? MapPointTypeInfo.NamePrefix(type) : point.name.Trim());
                    go.transform.SetParent(group, false);

                    // 부모(맵 루트 등)의 위치·스케일과 무관하게 계산한 월드 좌표에 정확히 놓는다.
                    go.transform.position = MapPointMath.ToWorld(mapOrigin, point.offset, alignedZ);
                }
            }
        }

        /// <summary>이전에 내보낸 프리팹(두 모드 모두)에서 포인트를 다시 읽는다.</summary>
        public static bool TryLoadPoints(string assetPath, Vector3 mapOrigin, out List<MapPointData> points, out string error)
        {
            points = new List<MapPointData>();
            error = null;

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                error = "프리팹을 불러오지 못했습니다: " + assetPath;
                return false;
            }

            var container = FindDeep(asset.transform, ContainerName);
            if (container == null)
            {
                error = "'" + ContainerName + "' 오브젝트가 없습니다. 이 툴로 내보낸 프리팹인지 확인하세요.";
                return false;
            }

            foreach (Transform group in container)
            {
                var hasGroupType = MapPointTypeInfo.TryParseGroup(group.name, out var groupType);
                foreach (Transform child in group)
                {
                    var type = groupType;
                    if (!hasGroupType && !MapPointTypeInfo.TryParseFromName(child.name, out type))
                    {
                        continue;
                    }

                    points.Add(new MapPointData(type, child.name, MapPointMath.ToOffset(mapOrigin, child.position)));
                }
            }

            return true;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
