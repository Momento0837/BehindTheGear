using NUnit.Framework;
using UnityEngine;

namespace BehindTheGear.Tools.MapPoints.Tests
{
    public sealed class MapPointMathTests
    {
        [Test]
        public void NextVersion_StartsAtOne_WhenNoFiles()
        {
            Assert.AreEqual(1, MapPointMath.GetNextVersion("#1_Forest", new string[0]));
        }

        [Test]
        public void NextVersion_IsMaxPlusOne_EvenWithGaps()
        {
            var files = new[] { "#1_Forest", "#1_Forest_Points_v1", "#1_Forest_Points_v3" };
            Assert.AreEqual(4, MapPointMath.GetNextVersion("#1_Forest", files));
        }

        [Test]
        public void NextVersion_IgnoresOtherMapsAndMalformedNames()
        {
            var files = new[] { "#1_Forest2_Points_v9", "#1_Forest_Points_vX", "#1_Forest_Points_v", "#1_Forest_Points_v2_old" };
            Assert.AreEqual(1, MapPointMath.GetNextVersion("#1_Forest", files));
        }

        [Test]
        public void NextVersion_IsCaseInsensitive_AndHandlesMultiDigit()
        {
            var files = new[] { "#1_forest_points_V12" };
            Assert.AreEqual(13, MapPointMath.GetNextVersion("#1_Forest", files));
        }

        [Test]
        public void BuildFileName_UsesPointsVersionToken()
        {
            Assert.AreEqual("#2_Garbage_Points_v5", MapPointMath.BuildFileName("#2_Garbage", 5));
        }

        [Test]
        public void IsExportName_DetectsToolOutputOnly()
        {
            Assert.IsTrue(MapPointMath.IsExportName("#1_Forest_Points_v1"));
            Assert.IsFalse(MapPointMath.IsExportName("#1_Forest"));
            Assert.IsFalse(MapPointMath.IsExportName("Points_Map"));
        }

        [Test]
        public void ToWorld_AddsOffsetToPrefabOrigin_AndUsesAlignedZ()
        {
            // #1_Forest 루트 (0, 4.87, -9.2), Default 레이어 스프라이트 월드 Z = -5.5
            var world = MapPointMath.ToWorld(new Vector3(0f, 4.87f, -9.2f), new Vector2(2f, -1f), -5.5f);
            Assert.AreEqual(2f, world.x, 1e-5f);
            Assert.AreEqual(3.87f, world.y, 1e-5f);
            Assert.AreEqual(-5.5f, world.z, 1e-5f);
        }

        [Test]
        public void ToOffset_IsInverseOfToWorld()
        {
            var origin = new Vector3(-3.25f, 1.5f, 7f);
            var offset = new Vector2(12.125f, -4.75f);
            var back = MapPointMath.ToOffset(origin, MapPointMath.ToWorld(origin, offset, 99f));
            Assert.AreEqual(offset.x, back.x, 1e-5f);
            Assert.AreEqual(offset.y, back.y, 1e-5f);
        }

        [Test]
        public void Snap_RoundsToStep_AndIgnoresNonPositiveStep()
        {
            Assert.AreEqual(new Vector2(1.5f, -0.5f), MapPointMath.Snap(new Vector2(1.6f, -0.4f), 0.5f));
            Assert.AreEqual(new Vector2(1.6f, -0.4f), MapPointMath.Snap(new Vector2(1.6f, -0.4f), 0f));
        }

        [Test]
        public void NextPointName_IncrementsPerType()
        {
            var names = new[] { "MonsterSpawn_01", "MonsterSpawn_07", "Portal_02", "Custom" };
            Assert.AreEqual("MonsterSpawn_08", MapPointMath.NextPointName(MapPointType.MonsterSpawn, names));
            Assert.AreEqual("Portal_03", MapPointMath.NextPointName(MapPointType.Portal, names));
            Assert.AreEqual("PlayerSpawn_01", MapPointMath.NextPointName(MapPointType.PlayerSpawn, names));
        }
    }
}
