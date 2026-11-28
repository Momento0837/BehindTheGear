using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BehindTheGear.Tools.MapPoints
{
    /// <summary>Z를 맞출 기준 레이어를 어떤 레이어 체계에서 찾을지.</summary>
    public enum ZLayerMode
    {
        /// <summary>Renderer의 Sorting Layer(이 프로젝트 맵의 게임플레이 레이어 = "Default").</summary>
        SortingLayer = 0,

        /// <summary>GameObject의 Layer(Physics/Culling 레이어).</summary>
        UnityLayer = 1,
    }

    /// <summary>미리보기에 그릴 스프라이트 1장(월드 좌표 기준으로 미리 계산해 둔다).</summary>
    internal sealed class MapSpriteItem
    {
        public Sprite sprite;
        public Rect uv;
        public Vector2 center;
        public Vector2 size;
        public float angle;
        public bool flipX;
        public bool flipY;
        public Color color;
        public int sortingLayerId;
        public string sortingLayerName;
        public int sortingLayerValue;
        public int sortingOrder;
        public float z;
        public int unityLayer;
    }

    /// <summary>맵 안의 Renderer 1개. Z 기준 후보 검색에 쓴다.</summary>
    internal sealed class MapRendererInfo
    {
        public string path;
        public float z;
        public string sortingLayerName;
        public int unityLayer;
        public bool isSprite;
    }

    /// <summary>
    /// 맵 프리팹을 읽기 전용으로 분석한 결과. 프리팹 원본은 절대 수정하지 않는다.
    /// </summary>
    internal sealed class MapPreviewModel
    {
        public GameObject prefab;
        public string assetPath;
        public string baseName;
        public string folder;

        /// <summary>프리팹 루트의 저장된 좌표. 툴 화면의 중앙(0,0)에 해당한다.</summary>
        public Vector3 origin;

        public Rect worldBounds;
        public readonly List<MapSpriteItem> sprites = new List<MapSpriteItem>();
        public readonly List<MapRendererInfo> renderers = new List<MapRendererInfo>();
        public readonly List<string> sortingLayersUsed = new List<string>();
        public int unsupportedRendererCount;

        public static MapPreviewModel Load(string assetPath, out string error)
        {
            error = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                error = "프리팹을 불러오지 못했습니다: " + assetPath;
                return null;
            }

            var model = new MapPreviewModel
            {
                prefab = prefab,
                assetPath = assetPath,
                baseName = Path.GetFileNameWithoutExtension(assetPath),
                folder = (Path.GetDirectoryName(assetPath) ?? "Assets").Replace('\\', '/'),
                origin = prefab.transform.position,
            };

            model.Analyze();
            return model;
        }

        void Analyze()
        {
            var hasBounds = false;
            var min = Vector2.zero;
            var max = Vector2.zero;

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var info = new MapRendererInfo
                {
                    path = BuildPath(renderer.transform),
                    z = renderer.transform.position.z,
                    sortingLayerName = renderer.sortingLayerName,
                    unityLayer = renderer.gameObject.layer,
                    isSprite = renderer is SpriteRenderer,
                };
                renderers.Add(info);

                if (!sortingLayersUsed.Contains(renderer.sortingLayerName))
                {
                    sortingLayersUsed.Add(renderer.sortingLayerName);
                }

                var spriteRenderer = renderer as SpriteRenderer;
                if (spriteRenderer == null || spriteRenderer.sprite == null || spriteRenderer.sprite.texture == null)
                {
                    if (spriteRenderer == null)
                    {
                        unsupportedRendererCount++;
                    }

                    continue;
                }

                var item = BuildSpriteItem(spriteRenderer);
                sprites.Add(item);

                foreach (var corner in Corners(item))
                {
                    if (!hasBounds)
                    {
                        min = max = corner;
                        hasBounds = true;
                    }
                    else
                    {
                        min = Vector2.Min(min, corner);
                        max = Vector2.Max(max, corner);
                    }
                }
            }

            if (!hasBounds)
            {
                min = (Vector2)origin - Vector2.one * 5f;
                max = (Vector2)origin + Vector2.one * 5f;
            }

            worldBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);

            // Unity 2D 정렬 규칙과 같은 순서: Sorting Layer → Order in Layer → 카메라에서 먼 것(Z 큰 것) 먼저.
            sprites.Sort((a, b) =>
            {
                var compare = a.sortingLayerValue.CompareTo(b.sortingLayerValue);
                if (compare != 0)
                {
                    return compare;
                }

                compare = a.sortingOrder.CompareTo(b.sortingOrder);
                return compare != 0 ? compare : b.z.CompareTo(a.z);
            });
        }

        static MapSpriteItem BuildSpriteItem(SpriteRenderer renderer)
        {
            var sprite = renderer.sprite;
            var texture = sprite.texture;
            var rect = sprite.rect;
            var textureRect = sprite.textureRect;

            var localSize = renderer.drawMode == SpriteDrawMode.Simple
                ? rect.size / sprite.pixelsPerUnit
                : renderer.size;

            var pivotNormalized = new Vector2(
                rect.width > 0f ? sprite.pivot.x / rect.width : 0.5f,
                rect.height > 0f ? sprite.pivot.y / rect.height : 0.5f);

            var localCenter = Vector2.Scale(new Vector2(0.5f, 0.5f) - pivotNormalized, localSize);
            if (renderer.flipX)
            {
                localCenter.x = -localCenter.x;
            }

            if (renderer.flipY)
            {
                localCenter.y = -localCenter.y;
            }

            var matrix = renderer.transform.localToWorldMatrix;
            var right = (Vector2)matrix.MultiplyVector(Vector3.right);
            var up = (Vector2)matrix.MultiplyVector(Vector3.up);
            var flipX = renderer.flipX;

            // 음수 스케일(좌우 반전)은 회전 대신 flip으로 표현한다.
            if (right.x * up.y - right.y * up.x < 0f)
            {
                right = -right;
                flipX = !flipX;
            }

            return new MapSpriteItem
            {
                sprite = sprite,
                uv = new Rect(
                    textureRect.x / texture.width,
                    textureRect.y / texture.height,
                    textureRect.width / texture.width,
                    textureRect.height / texture.height),
                center = matrix.MultiplyPoint3x4(localCenter),
                size = new Vector2(localSize.x * right.magnitude, localSize.y * up.magnitude),
                angle = Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg,
                flipX = flipX,
                flipY = renderer.flipY,
                color = renderer.color,
                sortingLayerId = renderer.sortingLayerID,
                sortingLayerName = renderer.sortingLayerName,
                sortingLayerValue = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID),
                sortingOrder = renderer.sortingOrder,
                z = renderer.transform.position.z,
                unityLayer = renderer.gameObject.layer,
            };
        }

        static IEnumerable<Vector2> Corners(MapSpriteItem item)
        {
            var radians = item.angle * Mathf.Deg2Rad;
            var right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (item.size.x * 0.5f);
            var up = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * (item.size.y * 0.5f);
            yield return item.center - right - up;
            yield return item.center + right - up;
            yield return item.center + right + up;
            yield return item.center - right + up;
        }

        public bool MatchesLayer(MapSpriteItem item, ZLayerMode mode, string layerName)
        {
            return mode == ZLayerMode.SortingLayer
                ? item.sortingLayerName == layerName
                : LayerMask.LayerToName(item.unityLayer) == layerName;
        }

        public List<MapRendererInfo> FindZCandidates(ZLayerMode mode, string layerName)
        {
            var result = new List<MapRendererInfo>();
            foreach (var info in renderers)
            {
                var matches = mode == ZLayerMode.SortingLayer
                    ? info.sortingLayerName == layerName
                    : LayerMask.LayerToName(info.unityLayer) == layerName;
                if (matches)
                {
                    result.Add(info);
                }
            }

            return result;
        }

        string BuildPath(Transform target)
        {
            var path = target.name;
            var current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
