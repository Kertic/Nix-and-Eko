using NixAndEko.Level;
using NixAndEko.Util;
using UnityEditor;
using UnityEngine;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// A scene-view level editor: pick a block type, drag rectangles in the Scene view to place
    /// them, click to select and edit, and rebuild the playable scene from the resulting
    /// <see cref="LevelData"/> asset. Levels stay as data, so they diff and merge cleanly.
    /// </summary>
    public class LevelEditorWindow : EditorWindow
    {
        enum Tool { Place, Select, Erase, SetSpawn }

        [SerializeField] LevelData _level;
        [SerializeField] Tool _tool = Tool.Place;
        [SerializeField] BlockType _paintType = BlockType.Ground;
        [SerializeField] float _grid = 1f;
        [SerializeField] bool _autoRebuild = true;
        [SerializeField] int _selected = -1;

        Vector2 _scroll;
        bool _dragging;
        Vector2 _dragStart;
        Vector2 _dragEnd;

        [MenuItem("Tools/Nix & Eko/Level Editor", priority = 10)]
        public static void Open()
        {
            var w = GetWindow<LevelEditorWindow>("Level Editor");
            w.minSize = new Vector2(300f, 420f);
            w.Show();
        }

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            if (_level == null)
                _level = AssetDatabase.LoadAssetAtPath<LevelData>(TestLevelBuilder.LevelPath);
        }

        void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

        // ------------------------------------------------------------------ window UI
        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Level Asset", EditorStyles.boldLabel);
            _level = (LevelData)EditorGUILayout.ObjectField(_level, typeof(LevelData), false);

            if (_level == null)
            {
                EditorGUILayout.HelpBox("Assign a Level asset, or create one.", MessageType.Info);
                if (GUILayout.Button("Create New Level"))
                    CreateLevelAsset();
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tool", EditorStyles.boldLabel);
            _tool = (Tool)GUILayout.Toolbar((int)_tool, new[] { "Place", "Select", "Erase", "Spawn" });

            if (_tool == Tool.Place)
            {
                EditorGUILayout.Space(2);
                _paintType = (BlockType)EditorGUILayout.EnumPopup("Block", _paintType);
                EditorGUILayout.HelpBox(
                    "Drag in the Scene view to place a block of this type. A plain click places one " +
                    "at its default size.", MessageType.None);
            }
            else if (_tool == Tool.Select)
            {
                EditorGUILayout.HelpBox("Click a block to select it, then edit it below or drag its handle.",
                    MessageType.None);
            }
            else if (_tool == Tool.Erase)
            {
                EditorGUILayout.HelpBox("Click a block to delete it.", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Click to move the player spawn point.", MessageType.None);
            }

            EditorGUILayout.Space();
            _grid = EditorGUILayout.Slider("Grid Snap", _grid, 0f, 4f);
            _autoRebuild = EditorGUILayout.Toggle("Rebuild On Change", _autoRebuild);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Vector2 spawn = EditorGUILayout.Vector2Field("Player Spawn", _level.playerSpawn);
            float killY = EditorGUILayout.FloatField("Kill Y", _level.killY);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_level, "Edit Level");
                _level.playerSpawn = spawn;
                _level.killY = killY;
                Dirty();
            }
            EditorGUILayout.LabelField("Blocks", _level.blocks.Count.ToString());

            DrawSelectedBlockInspector();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Scene")) Rebuild();
                if (GUILayout.Button("Frame Level")) FrameLevel();
            }
            if (GUILayout.Button("Clear All Blocks") &&
                EditorUtility.DisplayDialog("Clear level?",
                    "Remove all " + _level.blocks.Count + " blocks from " + _level.name + "?", "Clear", "Cancel"))
            {
                Undo.RecordObject(_level, "Clear Level");
                _level.blocks.Clear();
                _selected = -1;
                Dirty();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawSelectedBlockInspector()
        {
            if (_selected < 0 || _selected >= _level.blocks.Count) return;
            LevelBlock blk = _level.blocks[_selected];

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Block", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var type = (BlockType)EditorGUILayout.EnumPopup("Type", blk.type);
            Vector2 pos = EditorGUILayout.Vector2Field("Position", blk.position);
            Vector2 size = EditorGUILayout.Vector2Field("Size", blk.size);

            int linkId = blk.linkId;
            Vector2 patrol = blk.patrolOffset;
            float speed = blk.speed;
            float minCharge = blk.minCharge;
            Vector2 openOffset = blk.openOffset;

            if (type == BlockType.MovingPlatform)
            {
                patrol = EditorGUILayout.Vector2Field("Patrol Offset", patrol);
                speed = EditorGUILayout.FloatField("Speed", speed);
            }
            if (type == BlockType.Gate)
            {
                openOffset = EditorGUILayout.Vector2Field("Open Offset", openOffset);
                linkId = EditorGUILayout.IntField("Link Id", linkId);
            }
            if (type == BlockType.TargetSwitch)
                linkId = EditorGUILayout.IntField("Link Id", linkId);
            if (type == BlockType.BreakableWall)
                minCharge = EditorGUILayout.Slider("Min Charge", minCharge, 0f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_level, "Edit Block");
                blk.type = type;
                blk.position = pos;
                blk.size = size;
                blk.patrolOffset = patrol;
                blk.speed = speed;
                blk.linkId = linkId;
                blk.minCharge = minCharge;
                blk.openOffset = openOffset;
                Dirty();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Duplicate"))
                {
                    Undo.RecordObject(_level, "Duplicate Block");
                    LevelBlock copy = blk.Clone();
                    copy.position += new Vector2(blk.size.x + 1f, 0f);
                    _level.blocks.Add(copy);
                    _selected = _level.blocks.Count - 1;
                    Dirty();
                }
                if (GUILayout.Button("Delete"))
                {
                    Undo.RecordObject(_level, "Delete Block");
                    _level.blocks.RemoveAt(_selected);
                    _selected = -1;
                    Dirty();
                }
            }
        }

        // ------------------------------------------------------------------ scene interaction
        void OnSceneGUI(SceneView view)
        {
            if (_level == null) return;

            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            DrawBlockOverlays();
            DrawSpawnMarker();

            if (_tool != Tool.Select)
                HandleUtility.AddDefaultControl(id); // take clicks so we do not select scene objects

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.alt:
                    OnMouseDown(MouseWorld(e), e);
                    break;

                case EventType.MouseDrag when e.button == 0 && _dragging:
                    _dragEnd = Snap(MouseWorld(e));
                    e.Use();
                    view.Repaint();
                    break;

                case EventType.MouseUp when e.button == 0 && _dragging:
                    _dragging = false;
                    CommitDrag();
                    e.Use();
                    break;
            }

            if (_dragging) DrawPendingRect();
            if (_tool == Tool.Select) DrawSelectedHandle();
        }

        void OnMouseDown(Vector2 world, Event e)
        {
            switch (_tool)
            {
                case Tool.Place:
                    _dragging = true;
                    _dragStart = _dragEnd = Snap(world);
                    e.Use();
                    break;

                case Tool.Erase:
                {
                    int hit = Pick(world);
                    if (hit >= 0)
                    {
                        Undo.RecordObject(_level, "Erase Block");
                        _level.blocks.RemoveAt(hit);
                        if (_selected == hit) _selected = -1;
                        Dirty();
                    }
                    e.Use();
                    break;
                }

                case Tool.Select:
                {
                    int hit = Pick(world);
                    if (hit >= 0)
                    {
                        _selected = hit;
                        Repaint();
                        e.Use();
                    }
                    break;
                }

                case Tool.SetSpawn:
                    Undo.RecordObject(_level, "Move Spawn");
                    _level.playerSpawn = Snap(world);
                    Dirty();
                    e.Use();
                    break;
            }
        }

        void CommitDrag()
        {
            Vector2 min = Vector2.Min(_dragStart, _dragEnd);
            Vector2 max = Vector2.Max(_dragStart, _dragEnd);
            Vector2 size = max - min;

            // A click (rather than a drag) places a default-sized block at the cursor.
            if (size.x < 0.25f || size.y < 0.25f)
            {
                size = LevelData.DefaultSize(_paintType);
                min = _dragStart - size * 0.5f;
            }

            var block = new LevelBlock
            {
                type = _paintType,
                position = min + size * 0.5f,
                size = size,
                openOffset = new Vector2(0f, size.y + 0.5f),
            };

            Undo.RecordObject(_level, "Place Block");
            _level.blocks.Add(block);
            _selected = _level.blocks.Count - 1;
            Dirty();
        }

        void DrawSelectedHandle()
        {
            if (_selected < 0 || _selected >= _level.blocks.Count) return;
            LevelBlock blk = _level.blocks[_selected];

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(blk.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_level, "Move Block");
                blk.position = Snap(new Vector2(moved.x, moved.y));
                Dirty();
            }
        }

        void DrawBlockOverlays()
        {
            for (int i = 0; i < _level.blocks.Count; i++)
            {
                LevelBlock blk = _level.blocks[i];
                Rect r = blk.Rect;
                Color c = ColorFor(blk.type);

                Handles.DrawSolidRectangleWithOutline(
                    new[]
                    {
                        new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin),
                        new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax),
                    },
                    new Color(c.r, c.g, c.b, i == _selected ? 0.55f : 0.25f),
                    i == _selected ? Color.white : c);

                if (blk.type == BlockType.MovingPlatform)
                    Handles.DrawDottedLine(blk.position, blk.position + blk.patrolOffset, 4f);
                if (blk.type == BlockType.Gate)
                    Handles.DrawDottedLine(blk.position, blk.position + blk.openOffset, 2f);
            }
        }

        void DrawSpawnMarker()
        {
            Handles.color = Palette.Green;
            Handles.DrawWireDisc(_level.playerSpawn, Vector3.forward, 0.6f);
            Handles.Label(_level.playerSpawn + new Vector2(0.8f, 0.4f), "Spawn");
            Handles.color = Color.white;
        }

        void DrawPendingRect()
        {
            Vector2 min = Vector2.Min(_dragStart, _dragEnd);
            Vector2 max = Vector2.Max(_dragStart, _dragEnd);
            Color c = ColorFor(_paintType);
            Handles.DrawSolidRectangleWithOutline(
                new[]
                {
                    new Vector3(min.x, min.y), new Vector3(max.x, min.y),
                    new Vector3(max.x, max.y), new Vector3(min.x, max.y),
                },
                new Color(c.r, c.g, c.b, 0.35f), Color.white);
        }

        // ------------------------------------------------------------------ helpers
        static Color ColorFor(BlockType t) => t switch
        {
            BlockType.OneWay => Palette.OneWay,
            BlockType.MovingPlatform => Palette.Moving,
            BlockType.Hazard => Palette.Hazard,
            BlockType.Checkpoint => Palette.Checkpoint,
            BlockType.Gate => Palette.Gate,
            BlockType.TargetSwitch => Palette.Switch,
            BlockType.BreakableWall => Palette.Breakable,
            _ => Palette.Ground,
        };

        static Vector2 MouseWorld(Event e)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            // Everything lives on the z = 0 plane.
            float t = Mathf.Approximately(ray.direction.z, 0f) ? 0f : -ray.origin.z / ray.direction.z;
            Vector3 p = ray.origin + ray.direction * t;
            return new Vector2(p.x, p.y);
        }

        Vector2 Snap(Vector2 v)
        {
            if (_grid <= 0.001f) return v;
            return new Vector2(Mathf.Round(v.x / _grid) * _grid, Mathf.Round(v.y / _grid) * _grid);
        }

        int Pick(Vector2 world)
        {
            // Topmost (last drawn) block wins.
            for (int i = _level.blocks.Count - 1; i >= 0; i--)
                if (_level.blocks[i].Rect.Contains(world))
                    return i;
            return -1;
        }

        void Dirty()
        {
            EditorUtility.SetDirty(_level);
            if (_autoRebuild) Rebuild();
            SceneView.RepaintAll();
            Repaint();
        }

        void Rebuild()
        {
            if (_level != null) TestLevelBuilder.BuildScene(_level);
        }

        void FrameLevel()
        {
            if (_level.blocks.Count == 0 || SceneView.lastActiveSceneView == null) return;

            Bounds bounds = new Bounds(_level.blocks[0].position, Vector3.zero);
            foreach (var blk in _level.blocks)
                bounds.Encapsulate(new Bounds(blk.position, blk.size));
            bounds.Encapsulate(_level.playerSpawn);
            bounds.Expand(4f);

            SceneView.lastActiveSceneView.Frame(bounds, false);
        }

        void CreateLevelAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Level", "Level", "asset", "Where should the level asset live?", "Assets/_Game/Data");
            if (string.IsNullOrEmpty(path)) return;

            var level = CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(level, path);
            AssetDatabase.SaveAssets();
            _level = level;
        }
    }
}
