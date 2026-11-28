using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BehindTheGear.Tools.MapPoints.Tests
{
    /// <summary>
    /// 실제 프리팹 에셋을 만들고 내보내기·버전·불러오기를 검증한다.
    /// 전용 임시 폴더만 사용하고 테스트가 끝나면 폴더째 삭제한다(프로젝트 맵은 건드리지 않음).
    /// </summary>
    public sealed class MapPointExporterTests
    {
        const string TempFolder = "Assets/__MapPointToolTests__";
        const string MapPath = TempFolder + "/TestMap.prefab";

        static readonly Vector3 RootPosition = new Vector3(0f, 4.87f, -9.2f);
        const float GameplayLocalZ = 3.7f;

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }

            AssetDatabase.CreateFolder("Assets", "__MapPointToolTests__");

            var root = new GameObject("TestMap");
            try
            {
                root.transform.position = RootPosition;

                var gameplay = new GameObject("Near", typeof(SpriteRenderer));
                gameplay.transform.SetParent(root.transform, false);
                gameplay.transform.localPosition = new Vector3(0f, 0f, GameplayLocalZ);
                gameplay.GetComponent<SpriteRenderer>().sortingLayerID = 0; // "Default"

                var other = SortingLayer.layers.FirstOrDefault(l => l.id != 0);
                if (other.id != 0)
                {
                    var background = new GameObject("Far", typeof(SpriteRenderer));
                    background.transform.SetParent(root.transform, false);
                    background.transform.localPosition = new Vector3(0f, 0f, 15f);
                    background.GetComponent<SpriteRenderer>().sortingLayerID = other.id;
                }

                PrefabUtility.SaveAsPrefabAsset(root, MapPath, out var saved);
                Assert.IsTrue(saved, "테스트용 맵 프리팹 저장 실패");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempFolder);
        }

        static List<MapPointData> SamplePoints()
        {
            return new List<MapPointData>
            {
                new MapPointData(MapPointType.PlayerSpawn, "PlayerSpawn_01", new Vector2(2f, -1f)),
                new MapPointData(MapPointType.MonsterSpawn, "MonsterSpawn_01", new Vector2(-6f, 0.5f)),
                new MapPointData(MapPointType.Portal, "Portal_01", new Vector2(20f, 0f)),
                new MapPointData(MapPointType.MiniBoss, "MiniBoss_01", new Vector2(10f, 1f)),
            };
        }

        [Test]
        public void Model_ResolvesDefaultSortingLayerZ()
        {
            var model = MapPreviewModel.Load(MapPath, out var error);
            Assert.IsNull(error);
            Assert.AreEqual(RootPosition, model.origin);

            var candidates = model.FindZCandidates(ZLayerMode.SortingLayer, "Default");
            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual(RootPosition.z + GameplayLocalZ, candidates[0].z, 1e-4f);
        }

        [Test]
        public void PointsOnly_ExportsEmptyObjectsAtAbsolutePositions()
        {
            var z = RootPosition.z + GameplayLocalZ;
            Assert.IsTrue(MapPointExporter.Export(MapPath, RootPosition, SamplePoints(), z, MapExportMode.PointsOnly, out var path, out var error), error);
            Assert.AreEqual(TempFolder + "/TestMap_Points_v1.prefab", path);

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.AreEqual(Vector3.zero, asset.transform.position);
            Assert.IsNull(asset.GetComponentInChildren<Renderer>(true), "포인트만 모드에는 맵 스프라이트가 없어야 함");

            var player = asset.transform.Find("MapPoints/PlayerSpawns/PlayerSpawn_01");
            Assert.IsNotNull(player);
            Assert.AreEqual(1, player.GetComponents<Component>().Length, "Transform만 가진 Empty Object여야 함");
            Assert.AreEqual(2f, player.position.x, 1e-4f);
            Assert.AreEqual(3.87f, player.position.y, 1e-4f);
            Assert.AreEqual(z, player.position.z, 1e-4f);
        }

        [Test]
        public void MapWithPoints_CopiesMap_AndLeavesOriginalUntouched()
        {
            var z = RootPosition.z + GameplayLocalZ;
            Assert.IsTrue(MapPointExporter.Export(MapPath, RootPosition, SamplePoints(), z, MapExportMode.MapWithPoints, out var path, out var error), error);

            var copy = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(copy.transform.Find("Near"), "맵 내용이 복제되어야 함");
            var portal = copy.transform.Find("MapPoints/Portals/Portal_01");
            Assert.IsNotNull(portal);
            Assert.AreEqual(20f, portal.position.x, 1e-4f);
            Assert.AreEqual(4.87f, portal.position.y, 1e-4f);
            Assert.AreEqual(z, portal.position.z, 1e-4f);

            var original = AssetDatabase.LoadAssetAtPath<GameObject>(MapPath);
            Assert.IsNull(original.transform.Find("MapPoints"), "원본 맵 프리팹은 수정되면 안 됨");
            Assert.AreNotEqual(AssetDatabase.AssetPathToGUID(MapPath), AssetDatabase.AssetPathToGUID(path));
        }

        [Test]
        public void Versions_IncrementAcrossModes_AndNeverOverwrite()
        {
            var z = RootPosition.z + GameplayLocalZ;
            MapPointExporter.Export(MapPath, RootPosition, SamplePoints(), z, MapExportMode.PointsOnly, out var first, out _);
            MapPointExporter.Export(MapPath, RootPosition, SamplePoints(), z, MapExportMode.MapWithPoints, out var second, out _);
            MapPointExporter.Export(MapPath, RootPosition, SamplePoints(), z, MapExportMode.PointsOnly, out var third, out _);

            StringAssert.EndsWith("_Points_v1.prefab", first);
            StringAssert.EndsWith("_Points_v2.prefab", second);
            StringAssert.EndsWith("_Points_v3.prefab", third);
            Assert.AreEqual(3, MapPointExporter.FindExistingVersions(TempFolder, "TestMap").Count);
        }

        [TestCase(MapExportMode.PointsOnly)]
        [TestCase(MapExportMode.MapWithPoints)]
        public void LoadPoints_RoundTripsOffsetsAndTypes(MapExportMode mode)
        {
            var source = SamplePoints();
            MapPointExporter.Export(MapPath, RootPosition, source, -5.5f, mode, out var path, out _);

            Assert.IsTrue(MapPointExporter.TryLoadPoints(path, RootPosition, out var loaded, out var error), error);
            Assert.AreEqual(source.Count, loaded.Count);
            foreach (var expected in source)
            {
                var actual = loaded.Single(p => p.name == expected.name);
                Assert.AreEqual(expected.type, actual.type);
                Assert.AreEqual(expected.offset.x, actual.offset.x, 1e-4f);
                Assert.AreEqual(expected.offset.y, actual.offset.y, 1e-4f);
            }
        }

        [Test]
        public void Export_FailsCleanly_WhenNoPoints()
        {
            Assert.IsFalse(MapPointExporter.Export(MapPath, RootPosition, new List<MapPointData>(), 0f, MapExportMode.PointsOnly, out var path, out var error));
            Assert.IsNull(path);
            Assert.IsNotEmpty(error);
            Assert.AreEqual(0, MapPointExporter.FindExistingVersions(TempFolder, "TestMap").Count);
        }
    }
}
