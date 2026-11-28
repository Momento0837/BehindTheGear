using System;
using UnityEngine;

namespace BehindTheGear.Tools.MapPoints
{
    /// <summary>
    /// 툴 안에서 편집하는 포인트 1개.
    /// 위치는 "맵 프리팹 중앙(루트 좌표)으로부터의 편차"로 저장하고,
    /// 내보낼 때 <see cref="MapPointMath.ToWorld"/>로 실제 좌표를 계산한다.
    /// </summary>
    [Serializable]
    public sealed class MapPointData
    {
        public string id;
        public MapPointType type;
        public string name;
        public Vector2 offset;

        public MapPointData()
        {
            id = Guid.NewGuid().ToString("N");
        }

        public MapPointData(MapPointType type, string name, Vector2 offset)
        {
            id = Guid.NewGuid().ToString("N");
            this.type = type;
            this.name = name;
            this.offset = offset;
        }

        public MapPointData Clone(string newName, Vector2 newOffset)
        {
            return new MapPointData(type, newName, newOffset);
        }
    }
}
