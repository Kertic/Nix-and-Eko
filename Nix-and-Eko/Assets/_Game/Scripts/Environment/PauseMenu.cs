using System.Collections.Generic;
using NixAndEko.Util;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Environment
{
    /// <summary>
    /// The pause menu overlay: Start / Escape pops it up, freezes gameplay by dropping
    /// <see cref="Time.timeScale"/> to zero, and offers a Controls submenu with click-to-rebind
    /// per binding. Runs on IMGUI so it needs no Canvas/prefab wiring — the loader spawns one at
    /// runtime and hands it the shared <see cref="InputActionAsset"/>. Reads the Pause action
    /// directly (not through <see cref="Player.PlayerInputReader"/>), so it still fires while the
    /// gameplay reader is muted during possession.
    ///
    /// Rebindings survive across sessions: every completed rebind is serialised to
    /// <see cref="PlayerPrefs"/> as JSON via <see cref="InputActionRebindingExtensions.SaveBindingOverridesAsJson"/>,
    /// and re-applied on <see cref="Awake"/> before any reader has a chance to sample.
    ///
    /// Glyph labels for gamepad bindings adapt to the currently-active controller: PlayStation
    /// glyphs on a DualShock/DualSense, Xbox glyphs on an XInput/Xbox pad, plain names otherwise.
    /// Falls back to the raw control path (e.g. "buttonWest") when the mapping doesn't know a
    /// short name, so a never-before-seen control still labels correctly enough to identify.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class PauseMenu : MonoBehaviour
    {
        public InputActionAsset inputActions;
        [Tooltip("Freeze the game (Time.timeScale = 0) while the menu is open.")]
        public bool freezeTimeScale = true;

        [Header("Style")]
        public Color backdropTint = new Color(0f, 0f, 0f, 0.65f);
        public Color panelTint    = new Color(0.05f, 0.07f, 0.12f, 0.96f);
        public Color accent       = new Color(0.85f, 0.9f, 1f, 1f);

        const string OverridesKey = "NixEko.InputOverrides.v1";

        enum Page { Root, Controls, Debug, Abilities }

        bool _open;
        Page _page = Page.Root;
        InputAction _pauseAction;
        InputActionMap _playerMap;
        InputActionRebindingExtensions.RebindingOperation _rebindOp;
        InputAction _rebindingAction;
        int _rebindingBindingIndex;
        float _savedTimeScale = 1f;
        Vector2 _controlsScroll;

        // ---- controller navigation ----
        // The pause menu is playable without a mouse: D-pad / left stick / arrow keys move focus,
        // South button / Enter clicks the focused item, East button / Escape cancels back (or
        // closes the menu from the root). Focus index is linear across whatever the current page
        // draws, in draw order — recounted every frame so page changes and dynamic controls
        // lists (rebind rows, buttons appearing/disappearing) stay in sync without hand-wiring.
        InputActionMap _uiMap;
        InputAction _navAction, _submitAction, _cancelAction;
        int _focusIndex;
        int _drawnFocusables;
        int _lastFocusableCount;
        bool _submitLatched;   // set in Update, consumed by the first focused button drawn this frame
        Page _pageBeforeInput;
        // A pending page change is applied at the top of the next frame's Update, before any
        // OnGUI pass draws. Flipping _page mid-OnGUI is a Unity IMGUI landmine — the Layout
        // event lays out page A's controls, the Repaint event tries to draw page B's, and the
        // control counts mismatch. Also, the still-latched Submit that clicked Root's Debug
        // button would then fire Debug's Back button (same index) in the very same OnGUI cycle.
        Page? _pendingPage;
        float _navRepeatTimer;
        float _navRepeatInitial = 0.35f;
        float _navRepeatFast = 0.14f;
        bool _navHeld;
        Texture2D _fillTex;

        public bool IsOpen => _open;

        /// <summary>True while any pause menu instance is currently open. Static so gameplay
        /// systems (input readers, animation ticks) can mute themselves without holding a
        /// reference to the menu — hitstop-style brief `Time.timeScale = 0` bursts don't touch
        /// this, so short freezes never look like a pause to input.</summary>
        public static bool IsGameplayPaused { get; private set; }

        void Start()
        {
            // Initialise in Start rather than Awake because the loader assigns `inputActions`
            // AFTER AddComponent (Awake would see null). Nothing samples the pause action before
            // the first Update, so Start is soon enough.
            EnsureBound();
        }

        void EnsureBound()
        {
            if (_pauseAction != null || inputActions == null) return;
            _playerMap = inputActions.FindActionMap("Player", throwIfNotFound: false);
            _pauseAction = _playerMap?.FindAction("Pause", throwIfNotFound: false);
            _uiMap = inputActions.FindActionMap("UI", throwIfNotFound: false);
            _navAction    = _uiMap?.FindAction("Navigate", throwIfNotFound: false);
            _submitAction = _uiMap?.FindAction("Submit",   throwIfNotFound: false);
            _cancelAction = _uiMap?.FindAction("Cancel",   throwIfNotFound: false);
            LoadOverrides();
        }

        void OnDisable()
        {
            // Never leave the game frozen if this component gets torn down while the menu is open.
            if (_open && freezeTimeScale) Time.timeScale = _savedTimeScale;
            _open = false;
            IsGameplayPaused = false;
            CancelRebindIfActive();
        }

        void Update()
        {
            EnsureBound();
            if (_pauseAction == null) return;

            // While actively listening for a rebind, swallow every menu input — otherwise the
            // same button you're binding would immediately fire pause/submit/cancel too.
            if (IsRebinding) return;

            // Apply any deferred page change that a button click queued last frame BEFORE reading
            // this frame's input, so navigation immediately targets the new page's items.
            if (_pendingPage.HasValue)
            {
                _page = _pendingPage.Value;
                _focusIndex = 0;
                _pageBeforeInput = _page;
                _pendingPage = null;
            }

            if (_pauseAction.WasPressedThisFrame())
            {
                SetOpen(!_open);
                return;
            }

            if (!_open)
            {
                // Menu closed: keep the latch clear so a press captured during rebind or a race
                // condition never lingers into a later menu open.
                _submitLatched = false;
                return;
            }

            // Sample navigation intent for this frame. Latched-and-consumed on the first focused
            // button drawn in OnGUI, so a single press triggers one click even across the multiple
            // OnGUI passes IMGUI runs per frame (Layout, Repaint, MouseMove).
            HandleNavigation();
        }

        // Note: no LateUpdate reset. LateUpdate runs between Update and OnGUI in Unity's frame
        // order, so clearing the latch there would wipe it before any FocusableButton had a
        // chance to see it. Instead, Update overwrites `_submitLatched` every frame — true on
        // the frame Submit is pressed, false otherwise — and FocusableButton consumes it on
        // first click so a single press never fires two buttons across a page transition.

        void HandleNavigation()
        {
            // Cancel: back to Root from any subpage, or close the menu from Root. Fires once
            // per press. Falls back to device state alongside the action so a broken usage-tag
            // binding or a rebound Cancel still gets us out of the menu.
            bool cancel = (_cancelAction != null && _cancelAction.WasPressedThisFrame())
                       || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
                       || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
            if (cancel)
            {
                // Cancel walks one level up the page hierarchy: Abilities → Debug → Root, and
                // Controls → Root. Any queued pending page also unwinds so a double-tap cancel
                // doesn't skip the level a click was still transitioning into.
                if (_page != Page.Root || _pendingPage.HasValue)
                {
                    Page from = _pendingPage ?? _page;
                    GoToPage(from == Page.Abilities ? Page.Debug : Page.Root);
                }
                else SetOpen(false);
                return;
            }

            // Submit: cache the press for FocusableButton to consume next OnGUI pass. Belt-and-
            // suspenders — the Submit InputAction should fire from buttonSouth's usage tag, but
            // some platforms/drivers don't populate that usage, so we also poll the device
            // directly. Whichever fires first wins; the latch is single-shot so a button that
            // matches both paths still only clicks once.
            bool submitFromAction = _submitAction != null && _submitAction.WasPressedThisFrame();
            bool submitFromPad    = Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
            bool submitFromKb     = Keyboard.current != null &&
                                    (Keyboard.current.enterKey.wasPressedThisFrame ||
                                     Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                                     Keyboard.current.spaceKey.wasPressedThisFrame);
            bool submit = submitFromAction || submitFromPad || submitFromKb;
            // Overwrite every frame (not just on press). OnGUI runs AFTER Update in Unity's
            // frame order and consumes the latch inline on the focused button; the next Update
            // will read a new (usually false) value from the input actions and clear it here.
            _submitLatched = submit;
            if (submit && logInput)
                Debug.Log($"[PauseMenu] Submit pressed — action={submitFromAction}, pad={submitFromPad}, kb={submitFromKb}, " +
                          $"focusIndex={_focusIndex}, page={_page}, drawnLastFrame={_lastFocusableCount}, " +
                          $"actionEnabled={( _submitAction != null && _submitAction.enabled)}, " +
                          $"uiMapEnabled={( _uiMap != null && _uiMap.enabled)}, gamepad={( Gamepad.current != null ? Gamepad.current.GetType().Name : "none")}");

            // Navigate: up/down cycles focus, with a first-press pulse then an accelerating
            // repeat while held (matching the pause menu feel most games ship with).
            Vector2 nav = _navAction != null ? _navAction.ReadValue<Vector2>() : Vector2.zero;
            // Same device-fallback pattern for navigation so a rebound / missing Navigate action
            // doesn't strand the pause menu without a way to move focus.
            if (nav.sqrMagnitude < 0.01f && Gamepad.current != null)
            {
                Vector2 stick = Gamepad.current.leftStick.ReadValue();
                Vector2 dpad  = Gamepad.current.dpad.ReadValue();
                nav = stick.sqrMagnitude > dpad.sqrMagnitude ? stick : dpad;
            }
            if (nav.sqrMagnitude < 0.01f && Keyboard.current != null)
            {
                float y = (Keyboard.current.upArrowKey.isPressed || Keyboard.current.wKey.isPressed ? 1f : 0f)
                        - (Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed ? 1f : 0f);
                nav = new Vector2(0f, y);
            }
            float dt = Time.unscaledDeltaTime;
            _navRepeatTimer -= dt;

            if (Mathf.Abs(nav.y) > 0.5f)
            {
                if (!_navHeld || _navRepeatTimer <= 0f)
                {
                    int step = nav.y > 0f ? -1 : 1;   // stick up = focus previous item
                    if (_lastFocusableCount > 0)
                    {
                        _focusIndex += step;
                        _focusIndex = ((_focusIndex % _lastFocusableCount) + _lastFocusableCount) % _lastFocusableCount;
                    }
                    _navRepeatTimer = _navHeld ? _navRepeatFast : _navRepeatInitial;
                    _navHeld = true;
                }
            }
            else
            {
                _navHeld = false;
                _navRepeatTimer = 0f;
            }
        }

        [Header("Debug")]
        [Tooltip("Log every navigation / submit / cancel event to the console so a broken menu " +
                 "can be traced from the log alone. Leave off in shipping.")]
        public bool logInput = true;

        /// <summary>
        /// Queue a page change for the next Update tick. Never assign <see cref="_page"/>
        /// directly from OnGUI code: a mid-OnGUI flip mismatches the Layout and Repaint
        /// passes (different pages, different control counts, "control N's position in a
        /// group with only N controls" error) and lets a still-latched Submit press fire a
        /// button on the new page in the same cycle (Root's Debug → Debug's Back, both at
        /// index 2). Deferring to Update keeps the whole OnGUI cycle on one page.
        /// </summary>
        void GoToPage(Page next)
        {
            _pendingPage = next;
            _submitLatched = false;   // any pending press was for the button that triggered this transition
        }

        void SetOpen(bool open)
        {
            if (_open == open) return;
            if (logInput) Debug.Log($"[PauseMenu] SetOpen({open}) — uiMap={( _uiMap != null ? "ok" : "null")}, submitAction={( _submitAction != null ? "ok" : "null")}");
            _open = open;
            _page = Page.Root;
            _pendingPage = null;   // opening/closing wipes any queued transition from the last session
            _focusIndex = 0;
            _pageBeforeInput = _page;
            IsGameplayPaused = open;

            // The UI map owns Navigate / Submit / Cancel, but we only need those enabled while
            // the menu is up. Leaving it disabled outside pause means gameplay-map bindings
            // never fight the UI bindings for the same controls (buttonSouth is Jump *and*
            // Submit, for example).
            if (open) _uiMap?.Enable();
            else _uiMap?.Disable();

            if (freezeTimeScale)
            {
                if (open) { _savedTimeScale = Time.timeScale; Time.timeScale = 0f; }
                else Time.timeScale = _savedTimeScale;
            }
        }

        // ================================================================== IMGUI
        void OnGUI()
        {
            if (!_open) return;

            // A page change zeroes focus so the highlight doesn't land on a random slot in the
            // new page's list. Track the previous page across passes rather than inside SetOpen —
            // the Root ↔ Controls transitions happen from clicks inside OnGUI itself.
            if (_page != _pageBeforeInput) { _focusIndex = 0; _pageBeforeInput = _page; }

            _drawnFocusables = 0;

            // Full-screen darken behind the panel.
            DrawFullScreen(backdropTint);

            // Centred panel.
            float w = Mathf.Min(560f, Screen.width * 0.7f);
            float h = Mathf.Min(560f, Screen.height * 0.8f);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.color = panelTint;
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(panel.x + 20f, panel.y + 20f, panel.width - 40f, panel.height - 40f));

            switch (_page)
            {
                case Page.Root:     DrawRootPage(); break;
                case Page.Controls: DrawControlsPage(); break;
                case Page.Debug:    DrawDebugPage(); break;
                case Page.Abilities: DrawAbilitiesPage(); break;
            }

            GUILayout.EndArea();

            // Layout pass counts focusables; use that count for the next Update's navigation wrap.
            if (Event.current.type == EventType.Layout) _lastFocusableCount = _drawnFocusables;

            // Submit is a per-frame edge event — consume once, then it's gone (LateUpdate also
            // clears it as a safety net if OnGUI didn't run this frame for any reason).
            if (Event.current.type == EventType.Used || Event.current.type == EventType.Repaint)
                _submitLatched = false;
        }

        /// <summary>
        /// A GUILayout button that participates in controller focus. Draws a bright ring around
        /// itself when it's the currently-focused item, and reports clicked=true either when the
        /// mouse hits it or when it was focused and Submit was pressed this frame. Order of
        /// declaration is the focus order.
        /// </summary>
        bool FocusableButton(GUIContent content, GUIStyle style, params GUILayoutOption[] opts)
        {
            int me = _drawnFocusables++;
            bool mouseClicked = GUILayout.Button(content, style, opts);

            if (Event.current.type == EventType.Repaint && me == _focusIndex)
            {
                Rect r = GUILayoutUtility.GetLastRect();
                DrawFocusRing(r);
            }

            bool submitClicked = false;
            if (me == _focusIndex && _submitLatched)
            {
                submitClicked = true;
                _submitLatched = false;   // one-shot: the focused item eats the press
                if (logInput)
                    Debug.Log($"[PauseMenu] FocusableButton fired via Submit — index={me}, page={_page}, event={Event.current.type}, label='{content.text}'");
            }
            if (mouseClicked && logInput)
                Debug.Log($"[PauseMenu] FocusableButton fired via mouse — index={me}, page={_page}, event={Event.current.type}, label='{content.text}'");
            return mouseClicked || submitClicked;
        }

        void DrawFocusRing(Rect r)
        {
            EnsureFillTex();
            var prev = GUI.color;
            GUI.color = accent;
            float t = 2f;
            GUI.DrawTexture(new Rect(r.x - t, r.y - t, r.width + 2 * t, t),               _fillTex);
            GUI.DrawTexture(new Rect(r.x - t, r.yMax,  r.width + 2 * t, t),               _fillTex);
            GUI.DrawTexture(new Rect(r.x - t, r.y - t, t,               r.height + 2 * t), _fillTex);
            GUI.DrawTexture(new Rect(r.xMax,  r.y - t, t,               r.height + 2 * t), _fillTex);
            GUI.color = prev;
        }

        void EnsureFillTex()
        {
            if (_fillTex != null) return;
            _fillTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _fillTex.SetPixel(0, 0, Color.white);
            _fillTex.filterMode = FilterMode.Point;
            _fillTex.Apply();
        }

        void DrawRootPage()
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = accent;
            GUILayout.Label("PAUSED", title, GUILayout.Height(40f));
            GUILayout.Space(20f);

            if (BigFocusable("Resume"))   SetOpen(false);
            GUILayout.Space(6f);
            if (BigFocusable("Controls")) GoToPage(Page.Controls);
            GUILayout.Space(6f);
            if (BigFocusable("Debug"))    GoToPage(Page.Debug);
            GUILayout.Space(6f);
            if (BigFocusable("Quit to Desktop"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        void DrawDebugPage()
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = accent;
            GUILayout.Label("DEBUG", title, GUILayout.Height(30f));
            GUILayout.Space(10f);

            // Toggles read their pref live each frame so button text flips instantly on click
            // without a mirror bool to keep in sync.
            string stateText = "State Labels: " + (Util.PlayerStateLabel.Enabled ? "On" : "Off");
            if (BigFocusable(stateText)) Util.PlayerStateLabel.Enabled = !Util.PlayerStateLabel.Enabled;

            GUILayout.Space(6f);
            string hudText = "HUD Text Panel: " + (Util.DebugHud.Enabled ? "On" : "Off");
            if (BigFocusable(hudText)) Util.DebugHud.Enabled = !Util.DebugHud.Enabled;

            GUILayout.Space(6f);
            if (BigFocusable("Abilities…")) GoToPage(Page.Abilities);

            GUILayout.Space(20f);
            if (BigFocusable("Back", 120f)) GoToPage(Page.Root);
        }

        void DrawAbilitiesPage()
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = accent;
            GUILayout.Label("ABILITIES", title, GUILayout.Height(30f));

            var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            hint.normal.textColor = new Color(0.8f, 0.85f, 0.95f, 0.9f);
            GUILayout.Label("Toggle unlocks for playtesting. Changes take effect immediately.", hint);
            GUILayout.Space(10f);

            // Each row is a checkmark-prefixed toggle button — clicking flips the underlying
            // PlayerPrefs-backed flag, which the gameplay systems (bow, glide, shade summon) read
            // live each frame, so a click here changes behavior on the very next input sample
            // without any restart, respawn, or component rewire.
            DrawAbilityToggle("Recall Arrow",         Util.PlayerAbilities.RecallArrow,
                v => Util.PlayerAbilities.RecallArrow = v);
            GUILayout.Space(6f);
            DrawAbilityToggle("Make Shade",           Util.PlayerAbilities.MakeShade,
                v => Util.PlayerAbilities.MakeShade = v);
            GUILayout.Space(6f);
            DrawAbilityToggle("Shade Fires Arrow",    Util.PlayerAbilities.ShadeFireArrow,
                v => Util.PlayerAbilities.ShadeFireArrow = v);
            GUILayout.Space(6f);
            DrawAbilityToggle("Glider",               Util.PlayerAbilities.Glider,
                v => Util.PlayerAbilities.Glider = v);

            GUILayout.Space(20f);
            if (BigFocusable("Back", 120f)) GoToPage(Page.Debug);
        }

        /// <summary>Draw one row on the Abilities page — a big focusable button whose label is
        /// prefixed with a filled/empty checkbox glyph reflecting the current state. Clicking it
        /// flips the flag via <paramref name="setter"/>. The checkbox glyph is monospace-safe
        /// (leading spaces are stripped by the label alignment) so all four rows line up.</summary>
        void DrawAbilityToggle(string label, bool value, System.Action<bool> setter)
        {
            string mark = value ? "☑" : "☐";   // ☑ / ☐
            if (BigFocusable($"{mark}  {label}")) setter(!value);
        }

        void DrawControlsPage()
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = accent;
            GUILayout.Label("CONTROLS", title, GUILayout.Height(30f));

            if (IsRebinding)
            {
                var wait = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                wait.normal.textColor = new Color(1f, 0.85f, 0.4f);
                GUILayout.Space(10f);
                GUILayout.Label($"Press any input to bind for '{_rebindingAction.name}'…\n(Escape cancels)", wait);
                if (BigFocusable("Cancel Rebind")) CancelRebindIfActive();
                GUILayout.Space(10f);
            }

            ControllerKind kind = DetectController();

            _controlsScroll = GUILayout.BeginScrollView(_controlsScroll);

            if (_playerMap != null)
            {
                foreach (var action in _playerMap.actions)
                {
                    // Skip stick / composite actions — they're not simple button bindings.
                    if (action.type != InputActionType.Button) continue;
                    DrawActionRow(action, kind);
                }
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            using (new GUILayout.HorizontalScope())
            {
                if (BigFocusable("Reset to Defaults", 240f)) ResetOverrides();
                if (BigFocusable("Back", 120f)) GoToPage(Page.Root);
            }
        }

        void DrawActionRow(InputAction action, ControllerKind kind)
        {
            var label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            label.normal.textColor = new Color(0.9f, 0.95f, 1f);

            GUILayout.Space(4f);
            GUILayout.Label(action.name, label);

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(10f);
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var b = action.bindings[i];
                    if (b.isComposite || b.isPartOfComposite) continue;

                    bool active = IsRebinding && _rebindingAction == action && _rebindingBindingIndex == i;
                    DrawBindingButton(action, i, b.effectivePath, kind, active);
                }
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>Draw one rebind button. If a graphic glyph exists for the control (PS shape,
        /// Xbox letter, D-Pad plus), that texture is rendered inside the button; otherwise the
        /// button falls back to the text label. Clicking starts the interactive rebind for that
        /// binding.</summary>
        void DrawBindingButton(InputAction action, int bindingIndex, string effectivePath, ControllerKind kind, bool active)
        {
            var kindMap = kind switch
            {
                ControllerKind.PS      => ControllerGlyphs.Kind.PS,
                ControllerKind.Xbox    => ControllerGlyphs.Kind.Xbox,
                _                      => ControllerGlyphs.Kind.Generic,
            };
            Texture2D glyphTex = ControllerGlyphs.For(effectivePath, kindMap);

            var btn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = active ? FontStyle.Italic : FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 4, 4),
            };

            const float height = 34f;
            const float minWidth = 90f;

            GUIContent content;
            if (active)
            {
                content = new GUIContent("…listening…");
            }
            else if (glyphTex != null)
            {
                // Icon inside a button; keep a short text tooltip so a hover-over still names
                // the control (helpful when the graphic alone is ambiguous, e.g. two triggers).
                content = new GUIContent(glyphTex, GlyphFor(effectivePath, kind));
            }
            else
            {
                content = new GUIContent(GlyphFor(effectivePath, kind));
            }

            if (FocusableButton(content, btn, GUILayout.MinWidth(minWidth), GUILayout.Height(height)) && !IsRebinding)
            {
                StartRebind(action, bindingIndex);
            }
        }

        bool BigFocusable(string label, float width = 0f)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
            var opts = width > 0f
                ? new[] { GUILayout.Height(34f), GUILayout.Width(width) }
                : new[] { GUILayout.Height(34f), GUILayout.ExpandWidth(true) };
            return FocusableButton(new GUIContent(label), style, opts);
        }

        void DrawFullScreen(Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        // ================================================================== rebind lifecycle
        bool IsRebinding => _rebindOp != null && !_rebindOp.completed && !_rebindOp.canceled;

        void StartRebind(InputAction action, int bindingIndex)
        {
            CancelRebindIfActive();

            _rebindingAction = action;
            _rebindingBindingIndex = bindingIndex;

            // The action itself has to be disabled during the rebind — the Input System refuses to
            // rewrite a binding on a live-enabled action. Re-enabled on complete/cancel below.
            bool wasEnabled = action.enabled;
            action.Disable();

            _rebindOp = action.PerformInteractiveRebinding(bindingIndex)
                // Cursor tracking, scroll delta, and stick motion aren't valid button bindings —
                // exclude them so the rebind doesn't latch onto random mouse movement mid-press.
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(op =>
                {
                    if (wasEnabled) action.Enable();
                    op.Dispose();
                    _rebindOp = null;
                    SaveOverrides();
                })
                .OnCancel(op =>
                {
                    if (wasEnabled) action.Enable();
                    op.Dispose();
                    _rebindOp = null;
                })
                .Start();
        }

        void CancelRebindIfActive()
        {
            if (_rebindOp == null) return;
            _rebindOp.Cancel();
            _rebindOp.Dispose();
            _rebindOp = null;
        }

        void ResetOverrides()
        {
            if (inputActions == null) return;
            inputActions.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(OverridesKey);
            PlayerPrefs.Save();
        }

        void SaveOverrides()
        {
            if (inputActions == null) return;
            PlayerPrefs.SetString(OverridesKey, inputActions.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        void LoadOverrides()
        {
            if (inputActions == null || !PlayerPrefs.HasKey(OverridesKey)) return;
            string json = PlayerPrefs.GetString(OverridesKey);
            if (!string.IsNullOrEmpty(json)) inputActions.LoadBindingOverridesFromJson(json);
        }

        // ================================================================== glyph mapping
        enum ControllerKind { PS, Xbox, Generic }

        static ControllerKind DetectController()
        {
            var gp = Gamepad.current;
            if (gp == null) return ControllerKind.Generic;

            // Detect by C# type name rather than depending on the specific packages — the
            // DualShock/XInput support packages define their own subclasses. Type-name substring
            // check means we still get correct glyphs whether the package is present or not
            // (the base Gamepad class only reports generic names, which we treat as Xbox-ish).
            string n = gp.GetType().Name;
            if (n.Contains("DualSense") || n.Contains("DualShock")) return ControllerKind.PS;
            if (n.Contains("Xbox") || n.Contains("XInput") || n.Contains("Xinput")) return ControllerKind.Xbox;

            // Product-string fallback — some drivers report a generic Gamepad but expose the
            // controller name; catch the common "Wireless Controller" (DualSense/DualShock) case.
            string product = gp.description.product ?? "";
            if (product.Contains("DualSense") || product.Contains("Wireless Controller")) return ControllerKind.PS;

            return ControllerKind.Xbox;
        }

        static readonly Dictionary<string, string> GamepadNames = new()
        {
            { "leftStick",       "L Stick" },
            { "rightStick",      "R Stick" },
            { "dpad",            "D-Pad" },
            { "dpad/up",         "D-Pad ↑" },
            { "dpad/down",       "D-Pad ↓" },
            { "dpad/left",       "D-Pad ←" },
            { "dpad/right",      "D-Pad →" },
        };

        static string GlyphFor(string effectivePath, ControllerKind kind)
        {
            if (string.IsNullOrEmpty(effectivePath)) return "(unbound)";
            int slash = effectivePath.IndexOf('/');
            if (slash < 0) return effectivePath;

            string device  = effectivePath.Substring(0, slash);
            string control = effectivePath.Substring(slash + 1);

            if (device.Contains("Keyboard"))
                return "[" + PrettyKey(control) + "]";
            if (device.Contains("Mouse"))
                return "Mouse " + PrettyMouse(control);

            if (device.Contains("Gamepad"))
            {
                switch (control)
                {
                    case "buttonSouth": return kind == ControllerKind.PS ? "Cross ✕"     : "A";
                    case "buttonEast":  return kind == ControllerKind.PS ? "Circle ○"    : "B";
                    case "buttonWest":  return kind == ControllerKind.PS ? "Square □"    : "X";
                    case "buttonNorth": return kind == ControllerKind.PS ? "Triangle △"  : "Y";
                    case "leftShoulder":    return kind == ControllerKind.PS ? "L1" : "LB";
                    case "rightShoulder":   return kind == ControllerKind.PS ? "R1" : "RB";
                    case "leftTrigger":     return kind == ControllerKind.PS ? "L2" : "LT";
                    case "rightTrigger":    return kind == ControllerKind.PS ? "R2" : "RT";
                    case "leftStickPress":  return kind == ControllerKind.PS ? "L3" : "LS-Click";
                    case "rightStickPress": return kind == ControllerKind.PS ? "R3" : "RS-Click";
                    case "start":           return kind == ControllerKind.PS ? "Options" : "Menu";
                    case "select":          return kind == ControllerKind.PS ? "Create"  : "View";
                }
                if (GamepadNames.TryGetValue(control, out var n)) return n;
                return control;
            }

            return effectivePath;
        }

        static string PrettyKey(string control) => control switch
        {
            "leftArrow"  => "←",
            "rightArrow" => "→",
            "upArrow"    => "↑",
            "downArrow"  => "↓",
            "space"      => "Space",
            "escape"     => "Esc",
            "leftShift"  => "L-Shift",
            "rightShift" => "R-Shift",
            "leftCtrl"   => "L-Ctrl",
            "rightCtrl"  => "R-Ctrl",
            "leftAlt"    => "L-Alt",
            "rightAlt"   => "R-Alt",
            "tab"        => "Tab",
            "enter"      => "Enter",
            "backquote"  => "`",
            _            => control.Length == 1 ? control.ToUpperInvariant() : control,
        };

        static string PrettyMouse(string control) => control switch
        {
            "leftButton"   => "L",
            "rightButton"  => "R",
            "middleButton" => "M",
            "scroll"       => "Wheel",
            _              => control,
        };
    }
}
