using System;
using UnityEngine;

namespace BehindTheGear.Tools.MapPoints
{
    /// <summary>맵 위에 찍을 수 있는 포인트 종류. 내보낸 프리팹에서는 그룹 이름으로 구분된다.</summary>
    public enum MapPointType
    {
        PlayerSpawn = 0,
        MonsterSpawn = 1,
        Portal = 2,
        MiniBoss = 3,
    }

    /// <summary>포인트 종류별 표시 이름, 색, 내보내기 이름 규칙을 한곳에서 관리한다.</summary>
    public static class MapPointTypeInfo
    {
        public static readonly MapPointType[] All =
        {
            MapPointType.PlayerSpawn,
            MapPointType.MonsterSpawn,
            MapPointType.Portal,
            MapPointType.MiniBoss,
        };

        public static string DisplayName(MapPointType type)
        {
            switch (type)
            {
                case MapPointType.PlayerSpawn: return "플레이어 스폰";
                case MapPointType.MonsterSpawn: return "몬스터 스폰";
                case MapPointType.Portal: return "포탈";
                case MapPointType.MiniBoss: return "미니보스";
                default: return type.ToString();
            }
        }

        /// <summary>툴바처럼 좁은 곳에 쓰는 짧은 이름.</summary>
        public static string ShortName(MapPointType type)
        {
            switch (type)
            {
                case MapPointType.PlayerSpawn: return "플레이어";
                case MapPointType.MonsterSpawn: return "몬스터";
                case MapPointType.Portal: return "포탈";
                case MapPointType.MiniBoss: return "미니보스";
                default: return type.ToString();
            }
        }

        /// <summary>내보낸 프리팹 안에서 같은 종류의 포인트를 묶는 부모 오브젝트 이름.</summary>
        public static string GroupName(MapPointType type)
        {
            switch (type)
            {
                case MapPointType.PlayerSpawn: return "PlayerSpawns";
                case MapPointType.MonsterSpawn: return "MonsterSpawns";
                case MapPointType.Portal: return "Portals";
                case MapPointType.MiniBoss: return "MiniBosses";
                default: return type + "s";
            }
        }

        /// <summary>새 포인트 이름 앞부분. 예: MonsterSpawn_03</summary>
        public static string NamePrefix(MapPointType type)
        {
            switch (type)
            {
                case MapPointType.PlayerSpawn: return "PlayerSpawn";
                case MapPointType.MonsterSpawn: return "MonsterSpawn";
                case MapPointType.Portal: return "Portal";
                case MapPointType.MiniBoss: return "MiniBoss";
                default: return type.ToString();
            }
        }

        public static string Glyph(MapPointType type)
        {
            switch (type)
            {
                case MapPointType.PlayerSpawn: return "P";
                case MapPointType.MonsterSpawn: return "M";
                case MapPointType.Portal: return "G";
                case MapPointType.MiniBoss: return "B";
                default: return "?";
            }
        }

        public static Color Color(MapPointType type)
        {
            switch (type)
            {
                case MapPointType.PlayerSpawn: return new Color(0.30f, 0.86f, 0.47f);
                case MapPointType.MonsterSpawn: return new Color(0.96f, 0.36f, 0.32f);
                case MapPointType.Portal: return new Color(0.36f, 0.66f, 1.00f);
                case MapPointType.MiniBoss: return new Color(1.00f, 0.62f, 0.16f);
                default: return UnityEngine.Color.white;
            }
        }

        public static KeyCode Shortcut(MapPointType type)
        {
            return KeyCode.Alpha1 + (int)type;
        }

        public static bool TryParseGroup(string groupName, out MapPointType type)
        {
            foreach (var candidate in All)
            {
                if (string.Equals(GroupName(candidate), groupName, StringComparison.OrdinalIgnoreCase))
                {
                    type = candidate;
                    return true;
                }
            }

            type = MapPointType.MonsterSpawn;
            return false;
        }

        public static bool TryParseFromName(string objectName, out MapPointType type)
        {
            // 더 긴 접두어가 먼저 비교되도록 정렬할 필요는 없다(서로 접두어 관계가 아님).
            foreach (var candidate in All)
            {
                if (objectName != null && objectName.StartsWith(NamePrefix(candidate), StringComparison.OrdinalIgnoreCase))
                {
                    type = candidate;
                    return true;
                }
            }

            type = MapPointType.MonsterSpawn;
            return false;
        }
    }
}
