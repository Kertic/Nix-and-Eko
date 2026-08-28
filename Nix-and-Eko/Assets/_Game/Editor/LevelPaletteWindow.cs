using NixAndEko.Level;
using UnityEditor;
using UnityEngine;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// The scene-first level authoring tool. Buttons spawn a <see cref="LevelBlockObject"/> of the
    /// picked type into the open scene — thereafter it's an ordinary GameObject: move it with the
    /// standard translate handle, duplicate with Ctrl+D, delete with Delete, undo/redo with the
    /// usual shortcuts. No data asset, no rebuild step, no bespoke tool state.
    ///
    /// The <b>Group Selected</b> button wraps the current selection in a <see cref="BlockGroup"/>
    /// so a run of same-typed blocks tiles as one continuous mass — the group knows the boundary,
    /// individual blocks don't have to line their sprites up by hand.
    ///
    /// New blocks are placed at the SceneView pivot when one is open, otherwise at the world
    /// origin. They're parented under the "Level" root if one exists in the scene, so bookkeeping
    /// stays tidy without imposing a rigid folder structure.
    /// </summary>
    public class LevelPaletteWindow : EditorWindow
    {
        Vector2 _scroll;

        [MenuItem("Tools/Nix & Eko/Level Palette", priority = 5)]
        public static void Open()
        {
            var w = GetWindow<LevelPaletteWindow>("Level Palette");
            w.minSize = new Vector2(220f, 320f);
            w.Show();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Add block", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click a button to drop a block at the SceneView pivot. Move it with the standard " +
                "translate handle, duplicate with Ctrl+D, delete with Delete. Fields (size, link " +
                "id, patrol offset, etc.) live on the block's Inspector.",
                MessageType.None);

            foreach (BlockType t in System.Enum.GetValues(typeof(BlockType)))
                if (GUILayout.Button(ButtonLabel(t))) SpawnBlock(t);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "A group renders its children as one tiled mass — the tiling knows the group's " +
                "boundary, so a run of Ground blocks looks like one continuous slab. Select the " +
                "blocks first, then click Group Selected. Ungroup restores per-block sprites.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(!HasSelectedBlocks()))
                if (GUILayout.Button("Group Selected")) GroupSelected();
            using (new EditorGUI.DisabledScope(!HasSelectedGroup()))
                if (GUILayout.Button("Ungroup Selected")) UngroupSelected();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
            LevelBlockObject[] sel = SelectedBlocks();
            EditorGUILayout.LabelField("Blocks selected", sel.Length.ToString());
            if (sel.Length > 0 && GUILayout.Button("Rebuild Visuals"))
                foreach (var b in sel) if (b != null) b.RebuildVisual();

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ spawn
        void SpawnBlock(BlockType t)
        {
            Vector3 pos = ScenePivot();
            pos = SnapToGrid(pos, 1f);

            var go = new GameObject(t.ToString());
            Undo.RegisterCreatedObjectUndo(go, "Add " + t + " Block");
            go.transform.position = pos;

            Transform parent = LevelParent();
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Add Block");

            var blk = Undo.AddComponent<LevelBlockObject>(go);
            blk.type = t;
            blk.size = LevelData.DefaultSize(t);
            blk.RebuildVisual();

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }

        static Vector3 SnapToGrid(Vector3 v, float grid)
        {
            if (grid <= 0.001f) return v;
            return new Vector3(Mathf.Round(v.x / grid) * grid,
                               Mathf.Round(v.y / grid) * grid, 0f);
        }

        static Vector3 ScenePivot()
        {
            var view = SceneView.lastActiveSceneView;
            return view != null ? view.pivot : Vector3.zero;
        }

        /// <summary>Find (or create) a top-level "Level" GameObject in the active scene to park
        /// new blocks under. Keeps the hierarchy readable without forcing a specific structure —
        /// the parent is optional; blocks work fine at scene root too.</summary>
        static Transform LevelParent()
        {
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Level") return root.transform;
            return null;
        }

        // ------------------------------------------------------------------ grouping
        void GroupSelected()
        {
            LevelBlockObject[] sel = SelectedBlocks();
            if (sel.Length == 0) return;

            // The group parent lives next to the blocks in the hierarchy: same parent as the
            // first selected block, so we don't re-home a chunk of the level into an unrelated
            // branch. Children are re-parented under the group with world position preserved.
            var groupGo = new GameObject("BlockGroup");
            Undo.RegisterCreatedObjectUndo(groupGo, "Group Blocks");
            Undo.SetTransformParent(groupGo.transform, sel[0].transform.parent, "Group Blocks");

            foreach (var b in sel)
                Undo.SetTransformParent(b.transform, groupGo.transform, "Group Blocks");

            var group = Undo.AddComponent<BlockGroup>(groupGo);
            group.RebuildFromChildren();
            Selection.activeGameObject = groupGo;
        }

        void UngroupSelected()
        {
            foreach (var go in Selection.gameObjects)
            {
                var g = go != null ? go.GetComponent<BlockGroup>() : null;
                if (g == null) continue;

                Transform parent = g.transform.parent;
                var children = new System.Collections.Generic.List<Transform>();
                foreach (Transform child in g.transform) children.Add(child);

                foreach (var child in children)
                    Undo.SetTransformParent(child, parent, "Ungroup Blocks");
                Undo.DestroyObjectImmediate(g.gameObject);
            }
        }

        // ------------------------------------------------------------------ selection helpers
        static bool HasSelectedBlocks() => SelectedBlocks().Length > 0;

        static bool HasSelectedGroup()
        {
            foreach (var go in Selection.gameObjects)
                if (go != null && go.GetComponent<BlockGroup>() != null) return true;
            return false;
        }

        static LevelBlockObject[] SelectedBlocks()
        {
            var list = new System.Collections.Generic.List<LevelBlockObject>();
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                var b = go.GetComponent<LevelBlockObject>();
                if (b != null) list.Add(b);
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ button copy
        static string ButtonLabel(BlockType t) => t switch
        {
            BlockType.Ground         => "Ground",
            BlockType.OneWay         => "One-Way Platform",
            BlockType.MovingPlatform => "Moving Platform",
            BlockType.Hazard         => "Hazard (Brambles)",
            BlockType.Checkpoint     => "Checkpoint (Shrine)",
            BlockType.Gate           => "Gate",
            BlockType.TargetSwitch   => "Target Switch",
            BlockType.BreakableWall  => "Breakable Wall",
            BlockType.EnemyWalker    => "Enemy: Walker",
            BlockType.EnemySlammer   => "Enemy: Slammer",
            _                        => t.ToString(),
        };
    }
}
