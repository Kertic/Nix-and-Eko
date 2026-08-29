using System.Collections.Generic;
using NixAndEko.Level;
using UnityEditor;
using UnityEngine;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// Regenerates <c>Assets/_Game/Data/SampleTutorial12.asset</c> — a linear, 12-room
    /// teaching level that introduces each mechanic in isolation and then layers them.
    ///
    /// <para><b>Progression</b></para>
    /// <list type="bullet">
    /// <item>Rooms 1–2: movement only, then teach knockback-as-jump (shoot down to pogo).</item>
    /// <item>Rooms 3–4: introduce the tether (tap L1 to dash to a stuck arrow).</item>
    /// <item>Room 5: combine movement + tether.</item>
    /// <item>Rooms 6–7: introduce the phantom (hold L1 to morph the arrow, aim, loose).</item>
    /// <item>Room 8: tether + phantom in one chamber — you can only do one per arrow, so
    /// the player must land between them. That's the sequencing tension.</item>
    /// <item>Rooms 9–11: escalate combinations with hazards, moving platforms, enemies.</item>
    /// <item>Room 12: finale that uses everything.</item>
    /// </list>
    ///
    /// Each room is a fully walled-off box; the rooms are spaced far apart along X so nothing
    /// visually stitches them together. The "transition" is a blue portal door at the right of
    /// each room: walking into it teleports Nix into the next room's entry point (see
    /// <see cref="NixAndEko.Environment.Door"/>). Rooms 6-8 and 10 keep their puzzle-gates —
    /// the gates now sit in front of the door, so the switch has to be hit before the exit
    /// opens. Room 12 has no door; it ends at the victory shrine.
    ///
    /// The layout lives here as code (typed helper calls) rather than in the asset itself so
    /// it's diff-friendly. Tweak, rerun <b>Tools ▸ Nix &amp; Eko ▸ Build 12 Tutorial Rooms</b>,
    /// and the asset is rewritten in place (a confirm dialog warns first).
    /// </summary>
    public static class TutorialLevelsBuilder
    {
        public const string AssetPath = "Assets/_Game/Data/SampleTutorial12.asset";

        // ---- layout constants ---------------------------------------------------------------
        /// <summary>Interior width of one room, wall-to-wall.</summary>
        const float RoomW = 32f;
        /// <summary>Interior height of one room, floor-top to ceiling-bottom.</summary>
        const float RoomH = 15f;
        /// <summary>Thickness of the walls, floors and ceilings.</summary>
        const float Thick = 2f;
        /// <summary>Centre-to-centre spacing between rooms along X. Bigger than the room width
        /// so there's a visible gap of empty world between rooms — the backdrop trees fill it —
        /// and no player-side scenario makes them look walkably connected.</summary>
        const float RoomSpacing = 100f;

        /// <summary>Height of the puzzle-gates that block door approach in the phantom rooms.
        /// Sized so the gate seals the whole doorframe.</summary>
        const float GateH = 5f;
        /// <summary>Open-offset for a gate that has to slide clear of the doorframe. Lifts by
        /// its full height plus a hair so nothing catches.</summary>
        const float GateOpenY = GateH + 1f;

        /// <summary>World x of the centre of room <paramref name="i"/> (0-indexed).</summary>
        static float RoomCenterX(int i) => i * RoomSpacing;

        /// <summary>Where Nix appears when entering room <paramref name="i"/> — three units
        /// inside the west wall, standing on the floor. Matches the door target for the room
        /// that led into it.</summary>
        static Vector2 EntryPoint(int i) => new Vector2(RoomCenterX(i) - RoomW * 0.5f + 3f, 2f);

        /// <summary>World position of the exit door for room <paramref name="i"/> — pinned to
        /// the east wall (its east edge sits flush with the wall), so no per-room content ever
        /// reaches into the door's trigger volume and fires it prematurely. Floor-anchored,
        /// matching the Door block's bottom-anchor.</summary>
        static Vector2 DoorPosition(int i) => new Vector2(RoomCenterX(i) + RoomW * 0.5f - 0.75f, 1.5f);

        [MenuItem("Tools/Nix & Eko/Build 12 Tutorial Rooms", priority = 21)]
        public static void BuildAndOpen()
        {
            LevelData generated = Generate();

            LevelData existing = AssetDatabase.LoadAssetAtPath<LevelData>(AssetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, AssetPath);
            }
            else
            {
                if (!EditorUtility.DisplayDialog("Regenerate tutorial rooms?",
                        "Replace every block in SampleTutorial12.asset with the freshly generated layout? " +
                        "Any hand-made edits are lost.", "Regenerate", "Cancel"))
                    return;
                Undo.RecordObject(existing, "Regenerate Tutorial Rooms");
                existing.blocks = generated.blocks;
                existing.playerSpawn = generated.playerSpawn;
                existing.killY = generated.killY;
                EditorUtility.SetDirty(existing);
            }

            AssetDatabase.SaveAssets();
            var loaded = AssetDatabase.LoadAssetAtPath<LevelData>(AssetPath);
            Selection.activeObject = loaded;
            EditorGUIUtility.PingObject(loaded);
            TestLevelBuilder.BuildScene(loaded);
        }

        /// <summary>Build the <see cref="LevelData"/> in memory so tests and menu items share
        /// one source of truth for the layout.</summary>
        public static LevelData Generate()
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            level.playerSpawn = EntryPoint(0);
            level.killY = -30f;
            level.blocks = new List<LevelBlock>();

            var b = new Builder(level.blocks);
            BuildRoomShells(b, roomCount: 12);

            // Room bodies (0-indexed to match the layout math; comments call them "Room 1..12").
            BuildRoom1_MovementOnly(b, RoomCenterX(0));
            BuildRoom2_KnockbackJump(b, RoomCenterX(1));
            BuildRoom3_TetherIntro(b, RoomCenterX(2));
            BuildRoom4_TetherAcross(b, RoomCenterX(3));
            BuildRoom5_MovementAndTether(b, RoomCenterX(4));
            BuildRoom6_PhantomIntro(b, RoomCenterX(5));
            BuildRoom7_PhantomReach(b, RoomCenterX(6));
            BuildRoom8_TetherPlusPhantom(b, RoomCenterX(7));
            BuildRoom9_HazardCombo(b, RoomCenterX(8));
            BuildRoom10_MovingPlatforms(b, RoomCenterX(9));
            BuildRoom11_EnemyGauntlet(b, RoomCenterX(10));
            BuildRoom12_Finale(b, RoomCenterX(11));

            // Exit doors: every room but the last has one on its east side, teleporting Nix to
            // the entry point of the next room. Puzzle-gates that were previously carved into the
            // shared wall (rooms 6-8, 10) are stamped in per-room build as gates in front of the
            // door — the switch has to fire for the door to become approachable.
            for (int i = 0; i < 11; i++) AddExitDoor(b, i);

            return level;
        }

        // ============================================================ shell (walls, floors)
        /// <summary>
        /// Build the floor/ceiling and full-height east/west walls that box each room. Rooms
        /// share no geometry with their neighbours — each is its own sealed chamber, and the
        /// <see cref="BlockType.Door"/> a per-room build adds is what stitches them together.
        /// </summary>
        static void BuildRoomShells(Builder b, int roomCount)
        {
            for (int i = 0; i < roomCount; i++)
            {
                float cx = RoomCenterX(i);
                b.Ground(cx, -Thick * 0.5f, RoomW, Thick);                          // floor
                b.Ground(cx, RoomH + Thick * 0.5f, RoomW, Thick);                   // ceiling
                b.Ground(cx - RoomW * 0.5f - Thick * 0.5f, RoomH * 0.5f,            // west wall
                         Thick, RoomH + Thick * 2f);
                b.Ground(cx + RoomW * 0.5f + Thick * 0.5f, RoomH * 0.5f,            // east wall
                         Thick, RoomH + Thick * 2f);

                // Shrine just inside the west wall: a death anywhere in this room drops Nix here
                // instead of teleporting her back to the previous chamber.
                b.Checkpoint(cx - RoomW * 0.5f + 1.5f, 1f);
            }
        }

        /// <summary>Add the exit portal for room <paramref name="i"/>. The player walks into it
        /// and lands at the entry point of room i+1. Last room (i==11) gets no door — the finale
        /// ends at its shrine.</summary>
        static void AddExitDoor(Builder b, int i)
        {
            var pos = DoorPosition(i);
            b.Door(pos.x, pos.y, EntryPoint(i + 1));
            b.Sign(pos.x, pos.y + 2f, "→ Door to next room");
        }

        /// <summary>Add a switch-driven puzzle-gate that seals the exit door for room
        /// <paramref name="i"/>. The gate sits two units west of the door so the player can see
        /// the door through it. When the switch fires, the gate lifts clear.</summary>
        static void AddDoorLockGate(Builder b, int i, int linkId)
        {
            var pos = DoorPosition(i);
            b.Gate(pos.x - 2.5f, GateH * 0.5f, Thick, GateH, link: linkId, openY: GateOpenY);
        }

        // ============================================================ per-room content
        // Rooms are ~32 wide and 15 tall. Interior x-range is (cx - 15) .. (cx + 15) once the
        // doorway walls are subtracted. Floor top is at y=0, ceiling bottom at y=15.

        /// <summary>Room 1 — movement only. A/D and Space, plus a couple of hops.</summary>
        static void BuildRoom1_MovementOnly(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 1</b>\nA / D to move\nSpace to jump");
            b.Platform(cx - 6, 3, 5);
            b.Platform(cx, 5, 5);
            b.Platform(cx + 6, 3, 5);
            b.Sign(cx + 6, 6.5f, "Hop east →");
            // Small nub near the exit so the player has to jump the last step — a natural
            // "test what I just learned" beat before crossing into room 2.
            b.Ground(cx + 12, 1, 2, 2);
        }

        /// <summary>Room 2 — knockback-as-jump. Crossing the bramble pit requires pogoing off
        /// the west platform up to a mid-height stepping platform (out of normal-jump reach),
        /// then hopping down to the east platform. Anyone who tries to walk across the floor
        /// eats the brambles.</summary>
        static void BuildRoom2_KnockbackJump(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 2 — Pogo</b>\nMid-jump, aim down & fire.\nRecoil launches you up.");
            b.Hazard(cx - 3, 0.3f, 6);        // bramble pit

            b.Platform(cx - 10, 2, 5);        // west approach platform, jump-reachable from floor
            b.Platform(cx + 2, 5, 4);         // mid pogo platform (above the pit, too high to jump to)
            b.Sign(cx + 2, 6.5f, "Jump, shoot ↓,\nthe pogo lands you here");
            b.Platform(cx + 10, 2, 5);        // east platform, one hop down from mid
        }

        /// <summary>Room 3 — introduce the tether. A tall east-side wall is the arrow target;
        /// a ledge just west of the wall catches the player when the dash finishes. Shoot the
        /// wall, tap L1, land on the ledge, walk east to the doorway.</summary>
        static void BuildRoom3_TetherIntro(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 3 — Tether</b>\nShoot an arrow into a wall.\nTap L1 (Eko) to dash to it.");
            b.Platform(cx - 10, 3, 4);          // warm-up: draws the eye up and east
            b.Platform(cx - 2, 5, 4);
            b.Ground(cx + 12, 8, 3, 10);        // tall east wall, x=cx+10.5..cx+13.5, y=3..13
            b.Sign(cx + 11, 6f, "← Shoot me\nthen tap L1");
            b.Ground(cx + 7, 11, 6, 1);         // catch ledge, x=cx+4..cx+10, y=10.5..11.5
        }

        /// <summary>Room 4 — use the tether across a big hazard pit. The tether target is a
        /// stalactite hanging from the ceiling above the far landing pad, so a shot east from
        /// the take-off ledge embeds in the stalactite's west face; the dash lands the player
        /// on the pad, from which they walk east to the doorway.</summary>
        static void BuildRoom4_TetherAcross(Builder b, float cx)
        {
            b.Sign(cx - 12, 4.5f, "<b>ROOM 4</b>\nHit the stalactite,\ntap L1 to dash across.");
            b.Hazard(cx, 0.3f, 22);
            b.Ground(cx - 12, 3, 4, 1);         // take-off ledge
            b.Ground(cx + 12, 3, 4, 1);         // landing pad, x=cx+10..cx+14, y=2.5..3.5
            b.Ground(cx + 8, 10, 3, 8);         // stalactite, x=cx+6.5..cx+9.5, y=6..14
        }

        /// <summary>Room 5 — combine pogo and tether. Hop the pit on regular platforms, pogo up
        /// off the second platform to reach the mid ledge, then tether to the east wall so the
        /// dash drops you onto the exit ledge that leads to the doorway.</summary>
        static void BuildRoom5_MovementAndTether(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 5</b>\nPogo up, then tether\nto the east wall.");
            b.Hazard(cx - 8, 0.3f, 8);           // pit
            b.Platform(cx - 12, 2, 4);           // stepping platforms
            b.Platform(cx - 2, 3, 4);

            b.Ground(cx + 4, 7, 4, 1);           // mid ledge — reached by pogoing off the second platform

            b.Ground(cx + 13, 10, 2, 8);         // east tether wall, x=cx+12..cx+14, y=6..14
            b.Ground(cx + 8, 11, 8, 1);          // catch ledge beside the wall, x=cx+4..cx+12, y=10.5..11.5
        }

        /// <summary>Room 6 — introduce the phantom. Switch dangles above a wide overhanging
        /// shelf; direct arcs from the floor slam into the shelf's underside, so the player has
        /// to plant an arrow ON the shelf, hold L1 to morph, and fire the phantom at the switch
        /// from up there. The floor walkway stays clear so the only thing gating the exit is the
        /// puzzle itself.</summary>
        static void BuildRoom6_PhantomIntro(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 6 — Phantom</b>\nShoot the shelf.\n<b>HOLD</b> L1 to morph.\nAim, release to fire.");
            b.Platform(cx - 10, 3, 4);        // warm-up: draws the eye up toward the shelf
            b.Platform(cx - 4, 5, 4);
            b.Ground(cx + 4, 10, 10, 1);      // shelf, x=cx-1..cx+9, y=9.5..10.5
            b.Sign(cx + 2, 13.5f, "Shoot me\n(the switch)");
            b.Switch(cx + 2, 12f, link: 601); // switch pinned to ceiling above the shelf

            AddDoorLockGate(b, i: 5, linkId: 601);
        }

        /// <summary>Room 7 — phantom reach. Same trick as Room 6 (shelf hides a ceiling switch),
        /// but shifted east and higher so the player has to plant the arrow at a different angle.
        /// Adds a stepping-stone perch on the east side so the reload lands you near the exit.
        /// Floor stays clear so all the obstacles are ceiling-mounted.</summary>
        static void BuildRoom7_PhantomReach(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 7</b>\nSame trick, farther east.\nAim from the shelf.");
            b.Platform(cx - 10, 4, 5);
            b.Ground(cx + 8, 10, 10, 1);      // hood, x=cx+3..cx+13, y=9.5..10.5
            b.Switch(cx + 10, 12f, link: 701); // switch above the hood, near its east end

            AddDoorLockGate(b, i: 6, linkId: 701);

            // Landing platform below the hood so the reload after morphing isn't a floor slog.
            b.Platform(cx + 8, 6, 5);
        }

        /// <summary>Room 8 — tether AND phantom, in that order. Because each arrow gets exactly
        /// one Eko action (tap = tether, hold = phantom), the player uses them on different
        /// arrows: arrow 1 tethers up to the upper ledge; land, reload; arrow 2 morphs into the
        /// phantom and takes out the ceiling-hidden switch. Two beats, one chamber.</summary>
        static void BuildRoom8_TetherPlusPhantom(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 8</b>\nOne action per arrow.\n1) Tether up.\n2) Land, reload.\n3) Morph the next shot.");
            // Left half: hazard pit + a high mid ledge only reachable by tethering to the ceiling.
            b.Hazard(cx - 8, 0.3f, 8);
            b.Ground(cx - 13, 3, 3, 1);         // starting ledge
            b.Ground(cx - 2, 10, 8, 1);         // upper mid ledge, x=cx-6..cx+2, y=9.5..10.5
            b.Sign(cx - 2, 11.5f, "1st arrow tethers you here");

            // Right half: shelf-hides-switch pattern from rooms 6/7, positioned so the mid ledge
            // is the natural firing spot for the phantom shot.
            b.Ground(cx + 8, 7, 8, 1);          // overhang, x=cx+4..cx+12, y=6.5..7.5
            b.Switch(cx + 8, 9f, link: 801);
            b.Sign(cx + 8, 10.5f, "2nd arrow morphs\n→ hit switch");

            AddDoorLockGate(b, i: 7, linkId: 801);

            // Stepping stones down from the overhang so a successful phantom shot lands the
            // player near the exit rather than back at the pit. Kept west of the lock gate so
            // they don't intersect its collider.
            b.Platform(cx + 4, 4, 4);
            b.Platform(cx + 8, 3, 4);
        }

        /// <summary>Room 9 — escalate: hazard floor split by moving platforms, plus a walker
        /// on a suspended ledge. Cross via pogo or tether, take out the walker to reach the exit.</summary>
        static void BuildRoom9_HazardCombo(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 9</b>\nMoving platforms + brambles.\nBail-out ledge overhead\n(tether if you drift).");
            b.Hazard(cx - 6, 0.3f, 20);

            b.Ground(cx - 13, 2, 3, 1);
            b.Moving(cx - 6, 3, 3, patrol: new Vector2(0, 4), speed: 2f);
            b.Moving(cx + 2, 5, 3, patrol: new Vector2(4, 0), speed: 2.5f);
            b.Ground(cx + 11, 4, 4, 1);
            b.Walker(cx + 11, 5f);

            // A high tether shelf to bail out if the moving platforms get away from you.
            b.Ground(cx, 12, 6, 1);
        }

        /// <summary>Room 10 — moving-platform / phantom combo. Ride a lift up to line up a
        /// phantom shot at a switch tucked behind a ceiling notch.</summary>
        static void BuildRoom10_MovingPlatforms(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 10</b>\nRide the lift up.\nPhantom-shot the switch\nbehind the hanging hood.");
            b.Ground(cx - 13, 2, 3, 1);
            b.Moving(cx - 7, 4, 3, patrol: new Vector2(0, 6), speed: 2.5f);
            b.Moving(cx, 4, 3, patrol: new Vector2(0, 7), speed: 3f);
            // Landing ledge sits WEST of the lock gate — otherwise a player who never hits the
            // switch could ride the lifts up, drop east of the gate onto this ledge, and stroll
            // straight into the door.
            b.Ground(cx + 8, 2, 3, 1);

            // Hood hanging down from the ceiling hides the switch behind it. A shot fired
            // straight up hits the hood; a phantom planted past the hood's east edge can angle
            // back to strike the switch tucked between hood and ceiling.
            b.Ground(cx + 4, 11, 6, 1);      // hood, y=10.5..11.5 (well below the y=15 ceiling)
            b.Switch(cx + 5, 13f, link: 1001);
            AddDoorLockGate(b, i: 9, linkId: 1001);
        }

        /// <summary>Room 11 — enemy gauntlet in a tight vertical shaft; tether past the slammers
        /// rather than trading hits.</summary>
        static void BuildRoom11_EnemyGauntlet(Builder b, float cx)
        {
            b.Sign(cx - 12, 3.5f, "<b>ROOM 11</b>\nTether past the slammers.\nEnd with a charged shot\n(hold LMB fully).");
            b.Platform(cx - 10, 3, 4);
            b.Ground(cx - 4, 4, 5, 1);
            b.Slammer(cx - 4, 5f);

            b.Ground(cx + 4, 8, 5, 1);
            b.Slammer(cx + 4, 9f);

            b.Ground(cx - 4, 12, 5, 1);
            b.Walker(cx - 4, 13f);

            // A breakable wall gates the exit — reward reaching the top with a charged shot.
            b.Sign(cx + 13, 6f, "Charged shot → break");
            b.Breakable(cx + 13, 2.5f, 1.2f, 5, minCharge: 0.6f);
        }

        /// <summary>Room 12 — finale. Three phases: pogo-and-tether up the left wing to hit
        /// switch A, which drops a mid-divider gate; cross the hazard pit on moving platforms
        /// past a slammer; then phantom-snipe switch B in a right-side alcove to lower the gate
        /// hiding the victory shrine.</summary>
        static void BuildRoom12_Finale(Builder b, float cx)
        {
            b.Sign(cx - 14, 3.5f, "<b>ROOM 12 — Finale</b>\nEverything at once.\nGood luck.");
            // Left wing: mixed climb — pit, low ledge, pogo platform, tether ledge with switch A.
            b.Hazard(cx - 12, 0.3f, 6);
            b.Ground(cx - 14, 4, 2, 1);
            b.Platform(cx - 8, 6, 4);
            b.Ground(cx - 12, 10, 4, 1);
            b.Sign(cx - 12, 12.5f, "Switch A — opens the divider");
            b.Switch(cx - 12, 11f, link: 1201);

            // Mid-divider gate, opened by switch A. Tall (h=9) so it seals the whole opening
            // — the player has to complete the climb before they see the right wing.
            b.Gate(cx - 2, 4.5f, 2, 9, link: 1201, openY: 10f);

            // Middle arena: hazard floor spanned by two moving platforms, and a hovering slammer
            // guarding the crossing.
            b.Hazard(cx, 0.3f, 8);
            b.Moving(cx - 1, 4, 3, patrol: new Vector2(0, 6), speed: 2f);
            b.Moving(cx + 5, 6, 3, patrol: new Vector2(-4, 0), speed: 2.5f);
            b.Slammer(cx + 2, 9f);

            // Phantom-only switch B — same shelf-hides-ceiling-switch trick as rooms 6/7/8,
            // reused as a familiar beat during the finale rather than a fresh puzzle.
            b.Ground(cx + 8, 9, 10, 1);      // wide shelf, x=cx+3..cx+13, y=8.5..9.5
            b.Sign(cx + 8, 12.5f, "Switch B — phantom only");
            b.Switch(cx + 8, 11f, link: 1202);

            // Shrine alcove sealed by a gate against the room's right wall. Charged-shot
            // breakable in front so the player finishes on the archery beat.
            b.Gate(cx + 10, GateH * 0.5f, Thick, GateH, link: 1202, openY: GateOpenY);
            b.Breakable(cx + 14, 1.5f, 1.2f, 3, minCharge: 0.7f);
            b.Checkpoint(cx + 15, 1f);
            b.Sign(cx + 15, 3.5f, "★ You made it ★");
        }

        // ============================================================ builder helpers
        /// <summary>Small typed builder over a block list — mirrors <see cref="MetroidvaniaBuilder"/>'s
        /// helper so the two files read the same way. Only the methods this level needs are here.</summary>
        class Builder
        {
            readonly List<LevelBlock> _list;
            public Builder(List<LevelBlock> list) { _list = list; }

            public void Ground(float x, float y, float w, float h)
                => Add(BlockType.Ground, x, y, w, h);
            public void Platform(float x, float y, float w)
                => Add(BlockType.OneWay, x, y, w, 0.4f);
            public void Hazard(float x, float y, float w)
                => Add(BlockType.Hazard, x, y, w, 0.6f);
            public void Checkpoint(float x, float y)
                => Add(BlockType.Checkpoint, x, y, 1f, 2f);
            public void Walker(float x, float y)
                => Add(BlockType.EnemyWalker, x, y, 0.9f, 1f);
            public void Slammer(float x, float y)
                => Add(BlockType.EnemySlammer, x, y, 1f, 0.9f);

            public void Breakable(float x, float y, float w, float h, float minCharge)
            {
                var blk = Add(BlockType.BreakableWall, x, y, w, h);
                blk.minCharge = minCharge;
            }
            public void Moving(float x, float y, float w, Vector2 patrol, float speed)
            {
                var blk = Add(BlockType.MovingPlatform, x, y, w, 0.5f);
                blk.patrolOffset = patrol;
                blk.speed = speed;
            }
            public void Gate(float x, float y, float w, float h, int link, float openY)
            {
                var blk = Add(BlockType.Gate, x, y, w, h);
                blk.linkId = link;
                blk.openOffset = new Vector2(0f, openY);
            }
            public void Switch(float x, float y, int link)
            {
                var blk = Add(BlockType.TargetSwitch, x, y, 1f, 1f);
                blk.linkId = link;
            }
            public void Sign(float x, float y, string text)
            {
                var blk = Add(BlockType.Sign, x, y, 1f, 1f);
                blk.note = text;
            }
            public void Door(float x, float y, Vector2 target)
            {
                var blk = Add(BlockType.Door, x, y, 1.5f, 3f);
                blk.target = target;
            }

            LevelBlock Add(BlockType t, float x, float y, float w, float h)
            {
                var blk = new LevelBlock
                {
                    type = t,
                    position = new Vector2(x, y),
                    size = new Vector2(w, h),
                };
                _list.Add(blk);
                return blk;
            }
        }
    }
}
