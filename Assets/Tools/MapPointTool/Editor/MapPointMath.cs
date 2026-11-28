using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BehindTheGear.Tools.MapPoints
{
    /// <summary>
    /// Unity 오브젝트에 의존하지 않는 순수 계산 모음. EditMode 테스트로 검증한다.
    /// </summary>
    public static class MapPointMath
    {
        public const string VersionToken = "_Points_v";

        static readonly Regex AnyExportName = new Regex(@"_Points_v\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// 편차 → 실제 좌표. 맵 프리팹 중앙(루트 좌표)을 0으로 삼은 편차를
        /// 기존 프리팹 좌표값에 더하고, Z는 기준 레이어 스프라이트의 Z로 맞춘다.
        /// </summary>
        public static Vector3 ToWorld(Vector3 mapOrigin, Vector2 offset, float alignedZ)
        {
            return new Vector3(mapOrigin.x + offset.x, mapOrigin.y + offset.y, alignedZ);
        }

        /// <summary>실제 좌표 → 편차. <see cref="ToWorld"/>의 역연산(X/Y만).</summary>
        public static Vector2 ToOffset(Vector3 mapOrigin, Vector3 world)
        {
            return new Vector2(world.x - mapOrigin.x, world.y - mapOrigin.y);
        }

        public static Vector2 Snap(Vector2 value, float step)
        {
            if (step <= 0f)
            {
                return value;
            }

            return new Vector2(Mathf.Round(value.x / step) * step, Mathf.Round(value.y / step) * step);
        }

        /// <summary>"{base}_Points_v{n}" 형식이면 n을 돌려준다(대소문자 무시).</summary>
        public static bool TryParseVersion(string fileNameWithoutExtension, string baseName, out int version)
        {
            version = 0;
            if (string.IsNullOrEmpty(fileNameWithoutExtension) || string.IsNullOrEmpty(baseName))
            {
                return false;
            }

            var pattern = "^" + Regex.Escape(baseName) + Regex.Escape(VersionToken) + @"(\d+)$";
            var match = Regex.Match(fileNameWithoutExtension, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success
                   && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out version)
                   && version > 0;
        }

        /// <summary>폴더 안의 기존 버전 중 가장 큰 숫자 + 1. 하나도 없으면 1.</summary>
        public static int GetNextVersion(string baseName, IEnumerable<string> fileNamesWithoutExtension)
        {
            var max = 0;
            if (fileNamesWithoutExtension != null)
            {
                foreach (var fileName in fileNamesWithoutExtension)
                {
                    if (TryParseVersion(fileName, baseName, out var version) && version > max)
                    {
                        max = version;
                    }
                }
            }

            return max + 1;
        }

        public static string BuildFileName(string baseName, int version)
        {
            return baseName + VersionToken + version.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>이 툴이 만든 결과물(…_Points_vN)인지. 맵 목록에서 제외하는 데 쓴다.</summary>
        public static bool IsExportName(string fileNameWithoutExtension)
        {
            return !string.IsNullOrEmpty(fileNameWithoutExtension) && AnyExportName.IsMatch(fileNameWithoutExtension);
        }

        /// <summary>같은 종류 이름 중 가장 큰 번호 + 1로 새 이름을 만든다. 예: MonsterSpawn_04</summary>
        public static string NextPointName(MapPointType type, IEnumerable<string> existingNames)
        {
            var prefix = MapPointTypeInfo.NamePrefix(type);
            var pattern = new Regex("^" + Regex.Escape(prefix) + @"_(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var max = 0;
            if (existingNames != null)
            {
                foreach (var existing in existingNames)
                {
                    if (existing == null)
                    {
                        continue;
                    }

                    var match = pattern.Match(existing);
                    if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > max)
                    {
                        max = number;
                    }
                }
            }

            return prefix + "_" + (max + 1).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
