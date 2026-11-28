using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace BehindTheGear.Tools.MapPoints
{
    /// <summary>
    /// 맵 프리팹 위에 플레이어/몬스터 스폰, 포탈, 미니보스 위치를 찍고
    /// Empty Object 프리팹(…_Points_vN)으로 내보내는 에디터 창.
    /// </summary>
    public sealed class MapPointEditorWindow : EditorWindow
    {
        const string WindowTitle = "Map Point Tool";
        const string DefaultFolder = "Assets/!Map";
        const string PrefFolder = "BehindTheGear.MapPointTool.Folder";

        const float LeftWidth = 230f;
        const float RightWidth = 310f;
        const float ToolbarHeight = 22f;
        const float StatusHeight = 22f;
        const float MinZoom = 2f;
        const float MaxZoom = 800f;
        const float PointRadius = 9f;
        const int SelectTool = -1;

        static readonly Color CanvasBackground = new Color(0.115f, 0.12f, 0.135f);
        static readonly Color PanelLine = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color GridMinor = new Color(1f, 1f, 1f, 0.035f);
        static readonly Color GridMajor = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color AxisX = new Color(0.95f, 0.38f, 0.35f, 0.55f);
        static readonly Color AxisY = new Color(0.45f, 0.85f, 0.45f, 0.55f);
        static readonly Color Accent = new Color(0.36f, 0.66f, 1f);

        // ── 직렬화 상태(도메인 리로드·Undo 대상) ─────────────────────────────
        [SerializeField] string _folder = DefaultFolder;
        [SerializeField] string _mapPath;
        [SerializeField] List<MapPointData> _points = new List<MapPointData>();
        [SerializeField] string _selectedId;
        [SerializeField] int _tool = SelectTool;
        [SerializeField] Vector2 _viewCenter;
        [SerializeField] float _zoom = 40f;
        [SerializeField] bool _showGrid = true;
        [SerializeField] bool _showLabels = true;
        [SerializeField] bool _snap;
        [SerializeField] float _snapStep = 0.5f;
        [SerializeField] bool _dimOtherLayers;
        [SerializeField] List<string> _hiddenSortingLayers = new List<string>();
        [SerializeField] ZLayerMode _zMode = ZLayerMode.SortingLayer;
        [SerializeField] string _zLayerName = "Default";
        [SerializeField] int _zCandidateIndex;
        [SerializeField] bool _zManual;
        [SerializeField] float _zManualValue;
        [SerializeField] MapExportMode _exportMode = MapExportMode.PointsOnly;
        [SerializeField] bool _dirty;
        [SerializeField] bool _needsFrame;

        // ── 런타임 캐시 ─────────────────────────────────────────────────────
        MapPreviewModel _model;
        string _modelError;
        readonly List<string> _mapList = new List<string>();
        readonly Dictionary<string, int> _versionCounts = new Dictionary<string, int>();
        List<MapPointVersion> _versions = new List<MapPointVersion>();
        string _search = string.Empty;
        Vector2 _leftScroll;
        Vector2 _rightScroll;
        Vector2 _canvasSize;
        Vector2 _mouseScreen;
        bool _mouseInCanvas;
        string _statusMessage;
        MessageType _statusType;

        // 드래그 상태
        enum DragKind { None, Point, Pan }
        DragKind _drag;
        int _dragButton;
        bool _dragMoved;
        int _undoGroup;

        Texture2D _discTexture;
        Texture2D _ringTexture;
        GUIStyle _glyphStyle;
        GUIStyle _labelStyle;
        GUIStyle _sectionStyle;
        GUIStyle _rowNameStyle;
        GUIStyle _miniGrayStyle;
        GUIStyle _hintStyle;
        GUIStyle _bigButtonStyle;
        readonly Dictionary<MapPointType, GUIStyle> _toolStyles = new Dictionary<MapPointType, GUIStyle>();

        [MenuItem("Tools/BehindTheGear/Map Point Tool/Open", priority = 100)]
        public static void Open()
        {
            var window = GetWindow<MapPointEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(1180f, 600f);
            window.Show();
        }

        // ════════════════════════════════════════════════════════════════════
        // 수명 주기
        // ════════════════════════════════════════════════════════════════════

        void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            wantsMouseMove = true;
            saveChangesMessage = "내보내지 않은 맵 포인트가 있습니다.\n'Save'를 누르면 현재 설정으로 새 버전을 내보냅니다.";
            _folder = EditorPrefs.GetString(PrefFolder, string.IsNullOrEmpty(_folder) ? DefaultFolder : _folder);
            Undo.undoRedoPerformed += OnUndoRedo;

            RefreshMapList();
            if (!string.IsNullOrEmpty(_mapPath))
            {
                LoadModel(_mapPath);
            }
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            DestroyTexture(ref _discTexture);
            DestroyTexture(ref _ringTexture);
        }

        void OnProjectChange()
        {
            RefreshMapList();
            if (_model != null)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(_model.assetPath) == null)
                {
                    _model = null;
                    _modelError = "열려 있던 맵 프리팹이 이동되었거나 삭제되었습니다.";
                }
                else
                {
                    LoadModel(_model.assetPath);
                }
            }

            Repaint();
        }

        void OnUndoRedo()
        {
            Repaint();
        }

        public override void SaveChanges()
        {
            if (TryExport())
            {
                base.SaveChanges();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 데이터
        // ════════════════════════════════════════════════════════════════════

        void RefreshMapList()
        {
            _mapList.Clear();
            _versionCounts.Clear();

            if (string.IsNullOrEmpty(_folder) || !AssetDatabase.IsValidFolder(_folder))
            {
                return;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { _folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                if (MapPointMath.IsExportName(name))
                {
                    continue;
                }

                _mapList.Add(path);
                var folder = (Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');
                _versionCounts[path] = MapPointExporter.FindExistingVersions(folder, name).Count;
            }

            _mapList.Sort(System.StringComparer.OrdinalIgnoreCase);
        }

        void LoadModel(string path)
        {
            _model = MapPreviewModel.Load(path, out _modelError);
            RefreshVersions();
        }

        void RefreshVersions()
        {
            _versions = _model == null
                ? new List<MapPointVersion>()
                : MapPointExporter.FindExistingVersions(_model.folder, _model.baseName);
        }

        void OpenMap(string path)
        {
            if (string.IsNullOrEmpty(path) || path == _mapPath && _model != null)
            {
                return;
            }

            if (_dirty && _points.Count > 0 &&
                !EditorUtility.DisplayDialog(
                    "내보내지 않은 포인트",
                    "현재 맵에서 찍은 포인트를 아직 내보내지 않았습니다.\n다른 맵을 열면 이 포인트는 사라집니다. 계속할까요?",
                    "버리고 열기",
                    "취소"))
            {
                return;
            }

            Undo.RecordObject(this, "Open Map");
            _mapPath = path;
            _points.Clear();
            _selectedId = null;
            _dirty = false;
            _zCandidateIndex = 0;
            _statusMessage = null;
            LoadModel(path);
            _needsFrame = true;
            hasUnsavedChanges = false;
            Repaint();
        }

        MapPointData Selected
        {
            get { return string.IsNullOrEmpty(_selectedId) ? null : _points.FirstOrDefault(p => p.id == _selectedId); }
        }

        void RecordEdit(string label)
        {
            Undo.RecordObject(this, label);
            _dirty = true;
        }

        MapPointData AddPoint(MapPointType type, Vector2 offset)
        {
            RecordEdit("Add Map Point");
            var point = new MapPointData(type, MapPointMath.NextPointName(type, _points.Select(p => p.name)), offset);
            _points.Add(point);
            _selectedId = point.id;
            return point;
        }

        void DeletePoint(MapPointData point)
        {
            if (point == null)
            {
                return;
            }

            RecordEdit("Delete Map Point");
            _points.Remove(point);
            if (_selectedId == point.id)
            {
                _selectedId = null;
            }
        }

        void DuplicatePoint(MapPointData point)
        {
            if (point == null)
            {
                return;
            }

            RecordEdit("Duplicate Map Point");
            var copy = point.Clone(MapPointMath.NextPointName(point.type, _points.Select(p => p.name)), point.offset + new Vector2(1f, 0f));
            _points.Add(copy);
            _selectedId = copy.id;
        }

        bool TryResolveZ(out float z, out string source)
        {
            z = 0f;
            source = null;
            if (_model == null)
            {
                return false;
            }

            if (_zManual)
            {
                z = _zManualValue;
                source = "수동 지정";
                return true;
            }

            var candidates = _model.FindZCandidates(_zMode, _zLayerName);
            if (candidates.Count == 0)
            {
                return false;
            }

            var candidate = candidates[Mathf.Clamp(_zCandidateIndex, 0, candidates.Count - 1)];
            z = candidate.z;
            source = candidate.path;
            return true;
        }

        bool TryExport()
        {
            if (_model == null)
            {
                return false;
            }

            if (!TryResolveZ(out var z, out _))
            {
                SetStatus("Z 기준을 찾지 못해 내보내지 않았습니다. 기준 레이어를 확인하세요.", MessageType.Error);
                return false;
            }

            if (!MapPointExporter.Export(_model.assetPath, _model.origin, _points, z, _exportMode, out var path, out var error))
            {
                SetStatus("내보내기 실패: " + error, MessageType.Error);
                return false;
            }

            _dirty = false;
            hasUnsavedChanges = false;
            RefreshVersions();
            RefreshMapList();
            SetStatus("내보냄 → " + path, MessageType.Info);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
            }

            ShowNotification(new GUIContent(Path.GetFileName(path) + " 저장됨"), 1.5f);
            return true;
        }

        void LoadVersion(MapPointVersion version)
        {
            if (_model == null)
            {
                return;
            }

            if (_dirty && _points.Count > 0 &&
                !EditorUtility.DisplayDialog(
                    "버전 불러오기",
                    "현재 편집 중인 포인트를 v" + version.version + " 내용으로 바꿀까요?\n(Ctrl+Z로 되돌릴 수 있습니다)",
                    "불러오기",
                    "취소"))
            {
                return;
            }

            if (!MapPointExporter.TryLoadPoints(version.assetPath, _model.origin, out var loaded, out var error))
            {
                SetStatus("불러오기 실패: " + error, MessageType.Error);
                return;
            }

            Undo.RecordObject(this, "Load Map Points");
            _points = loaded;
            _selectedId = null;
            _dirty = false;
            SetStatus("v" + version.version + "에서 포인트 " + loaded.Count + "개를 불러왔습니다.", MessageType.Info);
        }

        void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
            Repaint();
        }

        // ════════════════════════════════════════════════════════════════════
        // OnGUI
        // ════════════════════════════════════════════════════════════════════

        void OnGUI()
        {
            EnsureResources();
            hasUnsavedChanges = _dirty && _points.Count > 0;

            HandleExternalDragAndDrop();
            HandleShortcuts();

            var full = position;
            var left = new Rect(0f, 0f, LeftWidth, full.height);
            var right = new Rect(full.width - RightWidth, 0f, RightWidth, full.height);
            var center = new Rect(LeftWidth, 0f, Mathf.Max(100f, full.width - LeftWidth - RightWidth), full.height);

            DrawCenter(center);
            DrawLeftPanel(left);
            DrawRightPanel(right);

            EditorGUI.DrawRect(new Rect(left.xMax - 1f, 0f, 1f, full.height), PanelLine);
            EditorGUI.DrawRect(new Rect(right.x, 0f, 1f, full.height), PanelLine);
        }

        void HandleExternalDragAndDrop()
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
            {
                return;
            }

            var path = DragAndDrop.objectReferences
                .Select(AssetDatabase.GetAssetPath)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase));
            if (path == null)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                OpenMap(path);
            }

            e.Use();
        }

        void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField || _model == null)
            {
                return;
            }

            if (e.control || e.command)
            {
                if (e.keyCode == KeyCode.D)
                {
                    DuplicatePoint(Selected);
                    e.Use();
                }

                return;
            }

            switch (e.keyCode)
            {
                case KeyCode.Q:
                    _tool = SelectTool;
                    break;
                case KeyCode.Escape:
                    _tool = SelectTool;
                    _selectedId = null;
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeletePoint(Selected);
                    break;
                case KeyCode.F:
                    FrameAll();
                    break;
                case KeyCode.G:
                    _showGrid = !_showGrid;
                    break;
                case KeyCode.S:
                    _snap = !_snap;
                    break;
                default:
                    var matched = false;
                    foreach (var type in MapPointTypeInfo.All)
                    {
                        if (e.keyCode == MapPointTypeInfo.Shortcut(type) || e.keyCode == KeyCode.Keypad1 + (int)type)
                        {
                            _tool = (int)type;
                            matched = true;
                        }
                    }

                    if (!matched)
                    {
                        return;
                    }

                    break;
            }

            e.Use();
            Repaint();
        }

        // ════════════════════════════════════════════════════════════════════
        // 가운데: 툴바 + 캔버스 + 상태줄
        // ════════════════════════════════════════════════════════════════════

        void DrawCenter(Rect rect)
        {
            var toolbar = new Rect(rect.x, rect.y, rect.width, ToolbarHeight);
            var canvas = new Rect(rect.x, toolbar.yMax, rect.width, rect.height - ToolbarHeight - StatusHeight);
            var status = new Rect(rect.x, canvas.yMax, rect.width, StatusHeight);

            DrawToolbar(toolbar);
            DrawCanvas(canvas);
            DrawStatusBar(status);
        }

        void DrawToolbar(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.toolbar);
            GUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(_model == null))
            {
                if (GUILayout.Toggle(_tool == SelectTool, new GUIContent("선택 Q", "포인트 선택·드래그 (Q)"), EditorStyles.toolbarButton))
                {
                    _tool = SelectTool;
                }

                foreach (var type in MapPointTypeInfo.All)
                {
                    var label = new GUIContent("● " + MapPointTypeInfo.ShortName(type) + " " + ((int)type + 1),
                        MapPointTypeInfo.DisplayName(type) + " 찍기 (단축키 " + ((int)type + 1) + ")");
                    var on = GUILayout.Toggle(_tool == (int)type, label, _toolStyles[type]);
                    if (on && _tool != (int)type)
                    {
                        _tool = (int)type;
                    }
                }

                GUILayout.FlexibleSpace();

                _showGrid = GUILayout.Toggle(_showGrid, new GUIContent("그리드", "G"), EditorStyles.toolbarButton);
                _showLabels = GUILayout.Toggle(_showLabels, "이름", EditorStyles.toolbarButton);
                _snap = GUILayout.Toggle(_snap, new GUIContent("스냅", "S — 격자 간격에 맞춰 배치"), EditorStyles.toolbarButton);
                using (new EditorGUI.DisabledScope(!_snap))
                {
                    _snapStep = Mathf.Max(0.01f, EditorGUILayout.FloatField(_snapStep, EditorStyles.toolbarTextField, GUILayout.Width(36f)));
                }

                _dimOtherLayers = GUILayout.Toggle(_dimOtherLayers, new GUIContent("강조", "Z 기준 레이어 외 스프라이트를 흐리게"), EditorStyles.toolbarButton);
                if (GUILayout.Button(new GUIContent("맞춤", "F — 맵 전체 보기"), EditorStyles.toolbarButton))
                {
                    FrameAll();
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawStatusBar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.18f));
            var inner = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, rect.height - 4f);

            string text;
            if (_model == null)
            {
                text = "맵 프리팹을 선택하세요.";
            }
            else if (_mouseInCanvas)
            {
                var offset = ScreenToOffset(_mouseScreen);
                if (_snap)
                {
                    offset = MapPointMath.Snap(offset, _snapStep);
                }

                var world = MapPointMath.ToWorld(_model.origin, offset, 0f);
                text = string.Format("편차 Δ({0:0.###}, {1:0.###})   →   좌표 ({2:0.###}, {3:0.###})      줌 {4:0}px/unit      포인트 {5}개",
                    offset.x, offset.y, world.x, world.y, _zoom, _points.Count);
            }
            else
            {
                text = string.Format("휠: 확대/축소   가운데·오른쪽 드래그 / Alt+드래그: 이동   1~4: 포인트 종류   Del: 삭제   Ctrl+D: 복제      포인트 {0}개", _points.Count);
            }

            GUI.Label(inner, text, _miniGrayStyle);
        }

        void DrawCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, CanvasBackground);

            if (_model == null)
            {
                var message = string.IsNullOrEmpty(_modelError)
                    ? "왼쪽 목록에서 맵 프리팹을 고르거나\n프리팹을 이 창으로 끌어다 놓으세요."
                    : _modelError;
                GUI.Label(rect, message, _hintStyle);
                return;
            }

            GUI.BeginClip(rect);
            var local = new Rect(0f, 0f, rect.width, rect.height);
            _canvasSize = local.size;

            if (_needsFrame && _canvasSize.x > 1f)
            {
                _needsFrame = false;
                FrameAll();
            }

            HandleCanvasInput(local);

            if (Event.current.type == EventType.Repaint)
            {
                if (_showGrid)
                {
                    DrawGrid(local);
                }

                DrawSprites(local);
                DrawOriginAndBounds();
                DrawPoints(local);
                DrawPlacementGhost();
                DrawLegend(local);
            }

            GUI.EndClip();
        }

        // ── 좌표 변환 ─────────────────────────────────────────────────────────
        // 캔버스 화면 좌표(픽셀, Y 아래로) ↔ 월드 좌표(유닛, Y 위로)

        Vector2 WorldToScreen(Vector2 world)
        {
            return _canvasSize * 0.5f + new Vector2(world.x - _viewCenter.x, _viewCenter.y - world.y) * _zoom;
        }

        Vector2 ScreenToWorld(Vector2 screen)
        {
            var delta = (screen - _canvasSize * 0.5f) / _zoom;
            return new Vector2(_viewCenter.x + delta.x, _viewCenter.y - delta.y);
        }

        Vector2 ScreenToOffset(Vector2 screen)
        {
            return ScreenToWorld(screen) - (Vector2)_model.origin;
        }

        Vector2 PointScreen(MapPointData point)
        {
            return WorldToScreen((Vector2)_model.origin + point.offset);
        }

        void FrameAll()
        {
            if (_model == null)
            {
                return;
            }

            var bounds = _model.worldBounds;
            foreach (var point in _points)
            {
                var world = (Vector2)_model.origin + point.offset;
                bounds = Rect.MinMaxRect(
                    Mathf.Min(bounds.xMin, world.x), Mathf.Min(bounds.yMin, world.y),
                    Mathf.Max(bounds.xMax, world.x), Mathf.Max(bounds.yMax, world.y));
            }

            var size = _canvasSize.x > 1f ? _canvasSize : new Vector2(position.width - LeftWidth - RightWidth, position.height - ToolbarHeight - StatusHeight);
            var zoomX = size.x / Mathf.Max(0.01f, bounds.width);
            var zoomY = size.y / Mathf.Max(0.01f, bounds.height);
            _zoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY) * 0.9f, MinZoom, MaxZoom);
            _viewCenter = bounds.center;
            Repaint();
        }

        // ── 입력 ─────────────────────────────────────────────────────────────

        void HandleCanvasInput(Rect local)
        {
            var e = Event.current;
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            var inside = local.Contains(e.mousePosition);

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseMove:
                    _mouseScreen = e.mousePosition;
                    _mouseInCanvas = inside;
                    Repaint();
                    break;

                case EventType.MouseLeaveWindow:
                    _mouseInCanvas = false;
                    Repaint();
                    break;

                case EventType.ScrollWheel:
                    if (!inside)
                    {
                        break;
                    }

                    var before = ScreenToWorld(e.mousePosition);
                    var factor = Mathf.Pow(1.12f, -e.delta.y / 3f);
                    _zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
                    _viewCenter += before - ScreenToWorld(e.mousePosition);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDown:
                    if (!inside)
                    {
                        break;
                    }

                    _mouseScreen = e.mousePosition;
                    _dragButton = e.button;
                    _dragMoved = false;

                    if (e.button == 2 || e.button == 1 || e.button == 0 && e.alt)
                    {
                        _drag = DragKind.Pan;
                    }
                    else if (e.button == 0)
                    {
                        BeginPointInteraction(e.mousePosition);
                    }

                    GUIUtility.hotControl = controlId;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId)
                    {
                        break;
                    }

                    _mouseScreen = e.mousePosition;
                    _dragMoved |= e.delta.sqrMagnitude > 0.01f;

                    if (_drag == DragKind.Pan)
                    {
                        _viewCenter += new Vector2(-e.delta.x, e.delta.y) / _zoom;
                    }
                    else if (_drag == DragKind.Point && Selected != null)
                    {
                        Undo.RecordObject(this, "Move Map Point");
                        _dirty = true;
                        Selected.offset = SnapOffset(ScreenToOffset(e.mousePosition));
                    }

                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId)
                    {
                        break;
                    }

                    GUIUtility.hotControl = 0;
                    if (_drag == DragKind.Point)
                    {
                        Undo.CollapseUndoOperations(_undoGroup);
                    }
                    else if (_drag == DragKind.Pan && _dragButton == 1 && !_dragMoved)
                    {
                        ShowContextMenu(e.mousePosition);
                    }

                    _drag = DragKind.None;
                    e.Use();
                    Repaint();
                    break;
            }
        }

        void BeginPointInteraction(Vector2 mouse)
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();

            var hit = HitTest(mouse);
            if (hit != null)
            {
                Undo.RecordObject(this, "Select Map Point");
                _selectedId = hit.id;
                _drag = DragKind.Point;
                return;
            }

            if (_tool != SelectTool)
            {
                AddPoint((MapPointType)_tool, SnapOffset(ScreenToOffset(mouse)));
                _drag = DragKind.Point;
                return;
            }

            _selectedId = null;
            _drag = DragKind.None;
        }

        Vector2 SnapOffset(Vector2 offset)
        {
            return _snap ? MapPointMath.Snap(offset, _snapStep) : offset;
        }

        MapPointData HitTest(Vector2 mouse)
        {
            // 위에 그려진(나중에 추가된) 포인트부터 검사한다.
            for (var i = _points.Count - 1; i >= 0; i--)
            {
                if ((PointScreen(_points[i]) - mouse).sqrMagnitude <= (PointRadius + 4f) * (PointRadius + 4f))
                {
                    return _points[i];
                }
            }

            return null;
        }

        void ShowContextMenu(Vector2 mouse)
        {
            var hit = HitTest(mouse);
            var menu = new GenericMenu();
            if (hit != null)
            {
                _selectedId = hit.id;
                menu.AddDisabledItem(new GUIContent(hit.name));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("복제  Ctrl+D"), false, () => DuplicatePoint(hit));
                foreach (var type in MapPointTypeInfo.All)
                {
                    var target = type;
                    menu.AddItem(new GUIContent("종류 변경/" + MapPointTypeInfo.DisplayName(type)), hit.type == type, () => ChangeType(hit, target));
                }

                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("삭제  Del"), false, () => DeletePoint(hit));
            }
            else
            {
                var offset = SnapOffset(ScreenToOffset(mouse));
                foreach (var type in MapPointTypeInfo.All)
                {
                    var target = type;
                    menu.AddItem(new GUIContent("여기에 추가/" + MapPointTypeInfo.DisplayName(type)), false, () => AddPoint(target, offset));
                }

                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("맵 전체 보기  F"), false, FrameAll);
            }

            menu.ShowAsContext();
        }

        void ChangeType(MapPointData point, MapPointType type)
        {
            if (point == null || point.type == type)
            {
                return;
            }

            RecordEdit("Change Map Point Type");
            var hadDefaultName = MapPointTypeInfo.TryParseFromName(point.name, out var nameType) && nameType == point.type;
            point.type = type;
            if (hadDefaultName)
            {
                point.name = MapPointMath.NextPointName(type, _points.Where(p => p != point).Select(p => p.name));
            }
        }

        // ── 그리기 ───────────────────────────────────────────────────────────

        void DrawGrid(Rect local)
        {
            // 화면에서 최소 14px 간격이 되도록 격자 간격을 1·2·5 배수로 자동 조절.
            var step = 1f;
            var multipliers = new[] { 2f, 2.5f, 2f };
            var index = 0;
            while (step * _zoom < 14f)
            {
                step *= multipliers[index++ % 3];
            }

            while (step * _zoom > 70f && step > 0.125f)
            {
                step *= 0.5f;
            }

            var origin = (Vector2)_model.origin;
            var minWorld = ScreenToWorld(new Vector2(0f, local.height));
            var maxWorld = ScreenToWorld(new Vector2(local.width, 0f));

            var startX = Mathf.Floor((minWorld.x - origin.x) / step);
            var endX = Mathf.Ceil((maxWorld.x - origin.x) / step);
            for (var i = startX; i <= endX; i++)
            {
                var x = WorldToScreen(new Vector2(origin.x + i * step, 0f)).x;
                var major = Mathf.Approximately(Mathf.Repeat(i, 5f), 0f);
                EditorGUI.DrawRect(new Rect(x, 0f, 1f, local.height), major ? GridMajor : GridMinor);
            }

            var startY = Mathf.Floor((minWorld.y - origin.y) / step);
            var endY = Mathf.Ceil((maxWorld.y - origin.y) / step);
            for (var i = startY; i <= endY; i++)
            {
                var y = WorldToScreen(new Vector2(0f, origin.y + i * step)).y;
                var major = Mathf.Approximately(Mathf.Repeat(i, 5f), 0f);
                EditorGUI.DrawRect(new Rect(0f, y, local.width, 1f), major ? GridMajor : GridMinor);
            }
        }

        void DrawSprites(Rect local)
        {
            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;

            foreach (var item in _model.sprites)
            {
                if (_hiddenSortingLayers.Contains(item.sortingLayerName))
                {
                    continue;
                }

                var center = WorldToScreen(item.center);
                var size = item.size * _zoom;
                var radius = size.magnitude * 0.5f;
                if (center.x + radius < 0f || center.x - radius > local.width || center.y + radius < 0f || center.y - radius > local.height)
                {
                    continue;
                }

                var uv = item.uv;
                if (item.flipX)
                {
                    uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
                }

                if (item.flipY)
                {
                    uv = new Rect(uv.x, uv.yMax, uv.width, -uv.height);
                }

                var color = item.color;
                if (_dimOtherLayers && !_model.MatchesLayer(item, _zMode, _zLayerName))
                {
                    color.a *= 0.18f;
                }

                GUI.matrix = previousMatrix;
                if (Mathf.Abs(item.angle) > 0.01f)
                {
                    GUIUtility.RotateAroundPivot(-item.angle, center);
                }

                GUI.color = color;
                GUI.DrawTextureWithTexCoords(new Rect(center - size * 0.5f, size), item.sprite.texture, uv, true);
            }

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        void DrawOriginAndBounds()
        {
            var bounds = _model.worldBounds;
            var min = WorldToScreen(new Vector2(bounds.xMin, bounds.yMax));
            var max = WorldToScreen(new Vector2(bounds.xMax, bounds.yMin));
            DrawRectOutline(Rect.MinMaxRect(min.x, min.y, max.x, max.y), new Color(1f, 1f, 1f, 0.22f));

            // 중앙(편차 0,0) = 프리팹 루트 좌표
            var origin = WorldToScreen(_model.origin);
            EditorGUI.DrawRect(new Rect(origin.x - 14f, origin.y, 28f, 1f), AxisX);
            EditorGUI.DrawRect(new Rect(origin.x, origin.y - 14f, 1f, 28f), AxisY);
            GUI.Label(new Rect(origin.x + 4f, origin.y + 2f, 120f, 16f), "중앙 (0, 0)", _miniGrayStyle);
        }

        void DrawPoints(Rect local)
        {
            var previousColor = GUI.color;
            foreach (var point in _points)
            {
                var screen = PointScreen(point);
                if (!Rect.MinMaxRect(-40f, -40f, local.width + 40f, local.height + 40f).Contains(screen))
                {
                    continue;
                }

                DrawMarker(screen, point.type, point.id == _selectedId, 1f);

                if (_showLabels || point.id == _selectedId)
                {
                    var content = new GUIContent(point.name);
                    var labelSize = _labelStyle.CalcSize(content);
                    var labelRect = new Rect(screen.x - labelSize.x * 0.5f, screen.y + PointRadius + 4f, labelSize.x, labelSize.y);
                    GUI.color = new Color(0f, 0f, 0f, 0.6f);
                    GUI.DrawTexture(new Rect(labelRect.x - 3f, labelRect.y, labelRect.width + 6f, labelRect.height), EditorGUIUtility.whiteTexture);
                    GUI.color = previousColor;
                    GUI.Label(labelRect, content, _labelStyle);
                }
            }

            GUI.color = previousColor;
        }

        void DrawPlacementGhost()
        {
            if (_tool == SelectTool || !_mouseInCanvas || _drag != DragKind.None || HitTest(_mouseScreen) != null)
            {
                return;
            }

            var offset = SnapOffset(ScreenToOffset(_mouseScreen));
            DrawMarker(WorldToScreen((Vector2)_model.origin + offset), (MapPointType)_tool, false, 0.45f);
        }

        void DrawMarker(Vector2 position, MapPointType type, bool selected, float alpha)
        {
            var previousColor = GUI.color;
            var previousMatrix = GUI.matrix;
            var color = MapPointTypeInfo.Color(type);
            var radius = type == MapPointType.MiniBoss ? PointRadius + 3f : PointRadius;
            if (selected)
            {
                radius += 2f;
            }

            // 그림자
            GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(position.x - radius - 2f, position.y - radius - 1f, (radius + 2f) * 2f, (radius + 2f) * 2f), _discTexture);

            color.a = alpha;
            GUI.color = color;
            if (type == MapPointType.Portal)
            {
                // 포탈은 마름모
                GUIUtility.RotateAroundPivot(45f, position);
                var half = radius * 0.78f;
                GUI.DrawTexture(new Rect(position.x - half, position.y - half, half * 2f, half * 2f), EditorGUIUtility.whiteTexture);
                GUI.matrix = previousMatrix;
            }
            else
            {
                GUI.DrawTexture(new Rect(position.x - radius, position.y - radius, radius * 2f, radius * 2f), _discTexture);
            }

            if (type == MapPointType.MiniBoss || selected)
            {
                GUI.color = selected ? new Color(1f, 1f, 1f, alpha) : new Color(0f, 0f, 0f, 0.5f * alpha);
                var ring = radius + (selected ? 4f : 0f);
                GUI.DrawTexture(new Rect(position.x - ring, position.y - ring, ring * 2f, ring * 2f), _ringTexture);
            }

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(position.x - radius, position.y - radius, radius * 2f, radius * 2f), MapPointTypeInfo.Glyph(type), _glyphStyle);
            GUI.color = previousColor;
        }

        void DrawLegend(Rect local)
        {
            var rect = new Rect(local.width - 150f, 10f, 140f, 14f + MapPointTypeInfo.All.Length * 16f);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.45f));
            var y = rect.y + 7f;
            foreach (var type in MapPointTypeInfo.All)
            {
                var count = _points.Count(p => p.type == type);
                var previous = GUI.color;
                GUI.color = MapPointTypeInfo.Color(type);
                GUI.DrawTexture(new Rect(rect.x + 8f, y + 3f, 9f, 9f), _discTexture);
                GUI.color = previous;
                GUI.Label(new Rect(rect.x + 22f, y, rect.width - 26f, 16f), MapPointTypeInfo.DisplayName(type) + "  " + count, _labelStyle);
                y += 16f;
            }
        }

        static void DrawRectOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        // ════════════════════════════════════════════════════════════════════
        // 왼쪽: 맵 프리팹 목록
        // ════════════════════════════════════════════════════════════════════

        void DrawLeftPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Space(6f);
            GUILayout.Label("맵 프리팹", _sectionStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(_folder, _folder), _miniGrayStyle, GUILayout.MaxWidth(LeftWidth - 76f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("폴더", "맵 프리팹을 찾을 폴더 변경"), EditorStyles.miniButtonLeft, GUILayout.Width(34f)))
            {
                ChooseFolder();
            }

            if (GUILayout.Button(new GUIContent("↻", "목록 새로고침"), EditorStyles.miniButtonRight, GUILayout.Width(24f)))
            {
                RefreshMapList();
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
            _search = GUILayout.TextField(_search ?? string.Empty, EditorStyles.toolbarSearchField);
            GUILayout.Space(4f);

            _leftScroll = GUILayout.BeginScrollView(_leftScroll);
            var shown = 0;
            var loadingPreview = false;
            foreach (var path in _mapList)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(_search) && name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                shown++;
                loadingPreview |= DrawMapRow(path, name);
            }

            if (shown == 0)
            {
                EditorGUILayout.HelpBox(AssetDatabase.IsValidFolder(_folder)
                    ? "이 폴더에 맵 프리팹이 없습니다."
                    : "폴더를 찾을 수 없습니다: " + _folder, MessageType.Info);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(4f);
            if (GUILayout.Button("다른 프리팹 열기…", GUILayout.Height(24f)))
            {
                BrowsePrefab();
            }

            GUILayout.Space(4f);
            GUILayout.Label("프리팹을 이 창에 끌어다 놓아도 열립니다.", _miniGrayStyle);
            GUILayout.Space(6f);
            GUILayout.EndArea();

            if (loadingPreview || AssetPreview.IsLoadingAssetPreviews())
            {
                Repaint();
            }
        }

        bool DrawMapRow(string path, string name)
        {
            var rowRect = GUILayoutUtility.GetRect(LeftWidth - 20f, 52f, GUILayout.ExpandWidth(true));
            var selected = _model != null && _model.assetPath == path;
            var hover = rowRect.Contains(Event.current.mousePosition);

            if (Event.current.type == EventType.Repaint)
            {
                if (selected)
                {
                    EditorGUI.DrawRect(rowRect, new Color(Accent.r, Accent.g, Accent.b, 0.28f));
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), Accent);
                }
                else if (hover)
                {
                    EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.05f));
                }
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var preview = asset != null ? AssetPreview.GetAssetPreview(asset) : null;
            var thumbRect = new Rect(rowRect.x + 8f, rowRect.y + 6f, 40f, 40f);
            EditorGUI.DrawRect(thumbRect, new Color(0f, 0f, 0f, 0.25f));
            if (preview != null)
            {
                GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit);
            }
            else
            {
                var mini = asset != null ? AssetPreview.GetMiniThumbnail(asset) : null;
                if (mini != null)
                {
                    GUI.DrawTexture(new Rect(thumbRect.x + 12f, thumbRect.y + 12f, 16f, 16f), mini);
                }
            }

            var versionCount = _versionCounts.TryGetValue(path, out var count) ? count : 0;
            var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
            GUI.Label(new Rect(thumbRect.xMax + 8f, rowRect.y + 8f, rowRect.width - 64f, 18f), name, _rowNameStyle);
            GUI.Label(new Rect(thumbRect.xMax + 8f, rowRect.y + 27f, rowRect.width - 64f, 16f),
                versionCount > 0 ? "포인트 버전 " + versionCount + "개 · " + folder : "포인트 없음 · " + folder, _miniGrayStyle);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                OpenMap(path);
                Event.current.Use();
            }

            if (hover)
            {
                Repaint();
            }

            return asset != null && preview == null;
        }

        void ChooseFolder()
        {
            var absolute = EditorUtility.OpenFolderPanel("맵 프리팹 폴더 선택", _folder, string.Empty);
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            var relative = ToProjectRelative(absolute);
            if (relative == null || !AssetDatabase.IsValidFolder(relative))
            {
                EditorUtility.DisplayDialog("폴더 선택", "현재 프로젝트의 Assets 폴더 안에서 골라 주세요.", "확인");
                return;
            }

            _folder = relative;
            EditorPrefs.SetString(PrefFolder, _folder);
            RefreshMapList();
        }

        void BrowsePrefab()
        {
            var absolute = EditorUtility.OpenFilePanel("맵 프리팹 열기", _folder, "prefab");
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            var relative = ToProjectRelative(absolute);
            if (relative == null || AssetDatabase.LoadAssetAtPath<GameObject>(relative) == null)
            {
                EditorUtility.DisplayDialog("프리팹 열기", "현재 프로젝트의 Assets 폴더 안에 있는 프리팹만 열 수 있습니다.", "확인");
                return;
            }

            OpenMap(relative);
        }

        static string ToProjectRelative(string absolutePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            var full = Path.GetFullPath(absolutePath).Replace('\\', '/');
            if (!full.StartsWith(projectRoot + "/Assets", System.StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return full.Substring(projectRoot.Length + 1);
        }

        // ════════════════════════════════════════════════════════════════════
        // 오른쪽: 인스펙터
        // ════════════════════════════════════════════════════════════════════

        void DrawRightPanel(Rect rect)
        {
            GUILayout.BeginArea(new Rect(rect.x + 1f, rect.y, rect.width - 1f, rect.height));
            _rightScroll = GUILayout.BeginScrollView(_rightScroll);
            GUILayout.Space(6f);

            if (_model == null)
            {
                GUILayout.Label("포인트 설정", _sectionStyle);
                EditorGUILayout.HelpBox("맵을 열면 Z 기준 레이어, 포인트 목록, 내보내기 설정이 여기에 표시됩니다.", MessageType.None);
            }
            else
            {
                DrawMapInfoSection();
                DrawZSection();
                DrawSelectedSection();
                DrawPointListSection();
                DrawValidationSection();
                DrawExportSection();
            }

            GUILayout.Space(10f);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Section(string title)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title, _sectionStyle);
            var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(1f, 1f, 1f, 0.08f));
            GUILayout.Space(4f);
        }

        void DrawMapInfoSection()
        {
            Section("맵");
            GUILayout.BeginHorizontal();
            GUILayout.Label(_model.baseName, _rowNameStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Project에서 보기"), EditorStyles.miniButton))
            {
                EditorGUIUtility.PingObject(_model.prefab);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(_model.assetPath, _miniGrayStyle);
            GUILayout.Space(2f);
            EditorGUILayout.LabelField("중앙(루트 좌표)", Format(_model.origin));
            EditorGUILayout.LabelField("맵 크기", string.Format("{0:0.##} × {1:0.##} unit", _model.worldBounds.width, _model.worldBounds.height));

            if (_model.unsupportedRendererCount > 0)
            {
                EditorGUILayout.HelpBox("스프라이트가 아닌 Renderer " + _model.unsupportedRendererCount + "개는 미리보기에 그려지지 않습니다(Z 기준 후보에는 포함).", MessageType.None);
            }

            if (_model.sortingLayersUsed.Count > 0)
            {
                GUILayout.Label("미리보기 레이어 표시", EditorStyles.miniBoldLabel);
                foreach (var layer in _model.sortingLayersUsed)
                {
                    var visible = !_hiddenSortingLayers.Contains(layer);
                    var newVisible = EditorGUILayout.ToggleLeft(layer + (layer == _zLayerName && _zMode == ZLayerMode.SortingLayer ? "   ← Z 기준" : string.Empty), visible);
                    if (newVisible != visible)
                    {
                        if (newVisible)
                        {
                            _hiddenSortingLayers.Remove(layer);
                        }
                        else
                        {
                            _hiddenSortingLayers.Add(layer);
                        }
                    }
                }
            }
        }

        void DrawZSection()
        {
            Section("Z 정렬 기준");

            var newMode = (ZLayerMode)EditorGUILayout.Popup("레이어 종류", (int)_zMode, new[] { "Sorting Layer", "Layer (GameObject)" });
            if (newMode != _zMode)
            {
                _zMode = newMode;
                _zCandidateIndex = 0;
            }

            // 프로젝트에 정의된 레이어 목록을 Unity API로 직접 읽어 온다.
            var layerNames = _zMode == ZLayerMode.SortingLayer
                ? SortingLayer.layers.Select(l => l.name).ToArray()
                : InternalEditorUtility.layers;
            var display = layerNames.Select(n =>
            {
                var used = _model.FindZCandidates(_zMode, n).Count;
                return used > 0 ? n + "  (맵에 " + used + "개)" : n;
            }).ToArray();
            var index = System.Array.IndexOf(layerNames, _zLayerName);
            var newIndex = EditorGUILayout.Popup("기준 레이어", index, display);
            if (newIndex != index && newIndex >= 0)
            {
                _zLayerName = layerNames[newIndex];
                _zCandidateIndex = 0;
            }

            if (!_zManual)
            {
                var candidates = _model.FindZCandidates(_zMode, _zLayerName);
                if (candidates.Count == 0)
                {
                    EditorGUILayout.HelpBox("이 맵에는 '" + _zLayerName + "' 레이어의 오브젝트가 없습니다. 다른 레이어를 고르거나 Z를 수동 지정하세요.", MessageType.Warning);
                }
                else if (candidates.Count > 1)
                {
                    var labels = candidates.Select(c => c.path.Replace('/', '∕') + "   z=" + c.z.ToString("0.###")).ToArray();
                    _zCandidateIndex = EditorGUILayout.Popup("기준 오브젝트", Mathf.Clamp(_zCandidateIndex, 0, candidates.Count - 1), labels);
                    if (candidates.Select(c => c.z).Distinct().Count() > 1)
                    {
                        EditorGUILayout.HelpBox("같은 레이어에 Z가 다른 오브젝트가 여러 개 있습니다. 기준 오브젝트를 확인하세요.", MessageType.Info);
                    }
                }
            }

            _zManual = EditorGUILayout.ToggleLeft("Z 수동 지정", _zManual);
            if (_zManual)
            {
                _zManualValue = EditorGUILayout.FloatField("Z 값", _zManualValue);
            }

            if (TryResolveZ(out var z, out var source))
            {
                var box = EditorGUILayout.GetControlRect(false, 34f);
                EditorGUI.DrawRect(box, new Color(Accent.r, Accent.g, Accent.b, 0.12f));
                GUI.Label(new Rect(box.x + 8f, box.y + 2f, box.width - 16f, 16f), "적용 Z = " + z.ToString("0.###"), EditorStyles.boldLabel);
                GUI.Label(new Rect(box.x + 8f, box.y + 17f, box.width - 16f, 16f), "출처: " + source, _miniGrayStyle);
            }
        }

        void DrawSelectedSection()
        {
            var point = Selected;
            Section("선택한 포인트");
            if (point == null)
            {
                GUILayout.Label("캔버스나 목록에서 포인트를 선택하세요.", _miniGrayStyle);
                return;
            }

            EditorGUI.BeginChangeCheck();
            var newName = EditorGUILayout.DelayedTextField("이름", point.name);
            var newType = (MapPointType)EditorGUILayout.Popup("종류", (int)point.type, MapPointTypeInfo.All.Select(MapPointTypeInfo.DisplayName).ToArray());
            var newOffset = EditorGUILayout.Vector2Field("편차 Δ (중앙 기준)", point.offset);
            if (EditorGUI.EndChangeCheck())
            {
                if (newType != point.type)
                {
                    ChangeType(point, newType);
                }
                else
                {
                    RecordEdit("Edit Map Point");
                    point.name = string.IsNullOrWhiteSpace(newName) ? point.name : newName.Trim();
                    point.offset = newOffset;
                }
            }

            var hasZ = TryResolveZ(out var z, out _);
            var world = MapPointMath.ToWorld(_model.origin, point.offset, z);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field("최종 좌표", hasZ ? world : new Vector3(world.x, world.y, float.NaN));
            }

            GUILayout.Label(string.Format("= 루트({0:0.###}, {1:0.###}) + Δ({2:0.###}, {3:0.###}),  Z = 기준 레이어",
                _model.origin.x, _model.origin.y, point.offset.x, point.offset.y), _miniGrayStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("화면 중앙으로", EditorStyles.miniButtonLeft))
            {
                _viewCenter = (Vector2)_model.origin + point.offset;
            }

            if (GUILayout.Button("복제", EditorStyles.miniButtonMid))
            {
                DuplicatePoint(point);
            }

            if (GUILayout.Button("삭제", EditorStyles.miniButtonRight))
            {
                DeletePoint(point);
            }

            GUILayout.EndHorizontal();
        }

        void DrawPointListSection()
        {
            Section("포인트 목록");
            if (_points.Count == 0)
            {
                GUILayout.Label("상단에서 포인트 종류(1~4)를 고르고 맵을 클릭하세요.", _miniGrayStyle);
            }

            MapPointData toDelete = null;
            foreach (var type in MapPointTypeInfo.All)
            {
                var ofType = _points.Where(p => p.type == type).ToList();

                var header = EditorGUILayout.GetControlRect(false, 18f);
                var previous = GUI.color;
                GUI.color = MapPointTypeInfo.Color(type);
                GUI.DrawTexture(new Rect(header.x + 2f, header.y + 4f, 10f, 10f), _discTexture);
                GUI.color = previous;
                GUI.Label(new Rect(header.x + 18f, header.y, header.width - 60f, 18f), MapPointTypeInfo.DisplayName(type) + "  (" + ofType.Count + ")", EditorStyles.miniBoldLabel);
                if (GUI.Button(new Rect(header.xMax - 40f, header.y + 1f, 40f, 16f), new GUIContent("+ 추가", "화면 중앙에 추가"), EditorStyles.miniButton))
                {
                    AddPoint(type, SnapOffset(_viewCenter - (Vector2)_model.origin));
                }

                foreach (var point in ofType)
                {
                    var row = EditorGUILayout.GetControlRect(false, 18f);
                    var selected = point.id == _selectedId;
                    if (selected)
                    {
                        EditorGUI.DrawRect(row, new Color(Accent.r, Accent.g, Accent.b, 0.25f));
                    }

                    if (GUI.Button(new Rect(row.x + 18f, row.y, row.width - 46f, row.height), GUIContent.none, GUIStyle.none))
                    {
                        _selectedId = point.id;
                        var screen = PointScreen(point);
                        if (screen.x < 0f || screen.y < 0f || screen.x > _canvasSize.x || screen.y > _canvasSize.y)
                        {
                            _viewCenter = (Vector2)_model.origin + point.offset;
                        }
                    }

                    GUI.Label(new Rect(row.x + 18f, row.y, 130f, row.height), point.name, EditorStyles.label);
                    GUI.Label(new Rect(row.x + 150f, row.y + 1f, row.width - 178f, row.height),
                        string.Format("Δ {0:0.##}, {1:0.##}", point.offset.x, point.offset.y), _miniGrayStyle);
                    if (GUI.Button(new Rect(row.xMax - 22f, row.y + 1f, 20f, 16f), new GUIContent("✕", "삭제"), EditorStyles.miniButton))
                    {
                        toDelete = point;
                    }
                }
            }

            if (toDelete != null)
            {
                DeletePoint(toDelete);
            }

            if (_points.Count > 0)
            {
                GUILayout.Space(4f);
                if (GUILayout.Button("모든 포인트 지우기", EditorStyles.miniButton) &&
                    EditorUtility.DisplayDialog("모든 포인트 지우기", "편집 중인 포인트 " + _points.Count + "개를 모두 지울까요?\n(Ctrl+Z로 되돌릴 수 있습니다)", "지우기", "취소"))
                {
                    RecordEdit("Clear Map Points");
                    _points.Clear();
                    _selectedId = null;
                }
            }
        }

        List<KeyValuePair<MessageType, string>> Validate(out bool blocking)
        {
            var messages = new List<KeyValuePair<MessageType, string>>();
            blocking = false;

            if (_points.Count == 0)
            {
                messages.Add(new KeyValuePair<MessageType, string>(MessageType.Error, "포인트가 없습니다."));
                blocking = true;
            }

            if (!TryResolveZ(out _, out _))
            {
                messages.Add(new KeyValuePair<MessageType, string>(MessageType.Error, "Z 기준을 정할 수 없습니다. 기준 레이어를 바꾸거나 수동 지정하세요."));
                blocking = true;
            }

            if (_points.Count > 0 && _points.All(p => p.type != MapPointType.PlayerSpawn))
            {
                messages.Add(new KeyValuePair<MessageType, string>(MessageType.Warning, "플레이어 스폰 포인트가 없습니다."));
            }

            var duplicates = _points.GroupBy(p => p.name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
            {
                messages.Add(new KeyValuePair<MessageType, string>(MessageType.Warning, "이름이 겹칩니다: " + string.Join(", ", duplicates)));
            }

            var outside = _points.Where(p => !_model.worldBounds.Contains((Vector2)_model.origin + p.offset)).Select(p => p.name).ToList();
            if (outside.Count > 0)
            {
                messages.Add(new KeyValuePair<MessageType, string>(MessageType.Warning, "맵 영역 밖에 있는 포인트: " + string.Join(", ", outside)));
            }

            return messages;
        }

        void DrawValidationSection()
        {
            var messages = Validate(out _);
            if (messages.Count == 0)
            {
                return;
            }

            Section("확인 필요");
            foreach (var message in messages)
            {
                EditorGUILayout.HelpBox(message.Value, message.Key);
            }
        }

        void DrawExportSection()
        {
            Section("내보내기");

            _exportMode = (MapExportMode)GUILayout.Toolbar((int)_exportMode, new[]
            {
                new GUIContent("포인트만", "Empty Object만 담은 프리팹(루트 0,0,0 · 포인트는 절대 좌표)"),
                new GUIContent("맵 + 포인트", "맵 프리팹을 복제하고 그 안에 포인트를 넣은 프리팹(원본 맵은 그대로)"),
            });
            GUILayout.Label(_exportMode == MapExportMode.PointsOnly
                ? "Empty Object만 담은 프리팹을 만듭니다. 원래 맵과 같은 씬에 (0,0,0)으로 두면 위치가 맞습니다."
                : "맵 프리팹 복제본(새 GUID)에 MapPoints를 추가합니다. 원본 맵 프리팹은 수정하지 않습니다.", _miniGrayStyle);

            var nextPath = MapPointExporter.GetNextOutputPath(_model.folder, _model.baseName, out _);
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("저장 위치", Path.GetFileName(nextPath));
            GUILayout.Label(_model.folder + "/", _miniGrayStyle);

            Validate(out var blocking);
            GUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(blocking))
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = Accent;
                if (GUILayout.Button("내보내기  →  " + Path.GetFileNameWithoutExtension(nextPath), _bigButtonStyle, GUILayout.Height(32f)))
                {
                    TryExport();
                }

                GUI.backgroundColor = previous;
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }

            GUILayout.Space(6f);
            GUILayout.Label("이 맵의 포인트 버전 (" + _versions.Count + ")", EditorStyles.miniBoldLabel);
            if (_versions.Count == 0)
            {
                GUILayout.Label("아직 내보낸 버전이 없습니다.", _miniGrayStyle);
            }

            for (var i = _versions.Count - 1; i >= 0; i--)
            {
                var version = _versions[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label("v" + version.version, EditorStyles.boldLabel, GUILayout.Width(34f));
                GUILayout.Label(Path.GetFileNameWithoutExtension(version.assetPath), _miniGrayStyle, GUILayout.MaxWidth(150f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("보기", EditorStyles.miniButtonLeft, GUILayout.Width(36f)))
                {
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(version.assetPath));
                }

                if (GUILayout.Button("불러오기", EditorStyles.miniButtonRight, GUILayout.Width(56f)))
                {
                    LoadVersion(version);
                }

                GUILayout.EndHorizontal();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 리소스
        // ════════════════════════════════════════════════════════════════════

        void EnsureResources()
        {
            if (_discTexture == null)
            {
                _discTexture = CreateCircleTexture(64, false);
            }

            if (_ringTexture == null)
            {
                _ringTexture = CreateCircleTexture(64, true);
            }

            if (_glyphStyle != null)
            {
                return;
            }

            _glyphStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            _glyphStyle.normal.textColor = Color.white;

            _labelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0) };
            _labelStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);

            _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, margin = new RectOffset(6, 6, 2, 2) };

            _rowNameStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };

            _miniGrayStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, clipping = TextClipping.Clip };
            _miniGrayStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.62f, 0.64f, 0.68f) : new Color(0.35f, 0.36f, 0.4f);

            _hintStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = true };
            _hintStyle.normal.textColor = new Color(0.6f, 0.62f, 0.66f);

            _bigButtonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 };

            _toolStyles.Clear();
            foreach (var type in MapPointTypeInfo.All)
            {
                var style = new GUIStyle(EditorStyles.toolbarButton);
                var color = MapPointTypeInfo.Color(type);
                style.normal.textColor = color;
                style.hover.textColor = color;
                style.onNormal.textColor = Color.Lerp(color, Color.white, 0.45f);
                style.onHover.textColor = Color.Lerp(color, Color.white, 0.45f);
                style.fontStyle = FontStyle.Bold;
                _toolStyles[type] = style;
            }
        }

        static Texture2D CreateCircleTexture(int size, bool ring)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[size * size];
            var radius = size * 0.5f;
            var thickness = size * 0.09f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    float alpha;
                    if (ring)
                    {
                        alpha = Mathf.Clamp01(radius - distance) * Mathf.Clamp01(distance - (radius - thickness));
                    }
                    else
                    {
                        alpha = Mathf.Clamp01(radius - distance);
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }

        static string Format(Vector3 value)
        {
            return string.Format("({0:0.###}, {1:0.###}, {2:0.###})", value.x, value.y, value.z);
        }
    }
}
