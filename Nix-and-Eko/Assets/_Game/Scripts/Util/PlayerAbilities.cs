using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Debug/dev toggles for the ability unlocks the player would earn through progression. The
    /// pause menu's Debug → Abilities submenu flips these; gameplay code (bow, glide, shade
    /// summon) checks the flags to decide whether the ability is available at all. Every flag
    /// defaults to ON so a fresh session plays with the full moveset — turning one off is a
    /// deliberate playtest lock, not the shipping default.
    ///
    /// Persisted in <see cref="PlayerPrefs"/> so a lock survives play-mode restarts and doesn't
    /// have to be re-set every time the editor reloads. The static getters read live each frame
    /// (cheap PlayerPrefs int lookups) so a toggle click takes effect on the very next input
    /// sample without a mirror bool to keep in sync — same pattern as
    /// <see cref="PlayerStateLabel.Enabled"/> and <see cref="DebugHud.Enabled"/>.
    /// </summary>
    public static class PlayerAbilities
    {
        const string RecallArrowKey    = "NixEko.Ability.RecallArrow.v1";
        const string MakeShadeKey      = "NixEko.Ability.MakeShade.v1";
        const string ShadeFireArrowKey = "NixEko.Ability.ShadeFireArrow.v1";
        const string GliderKey         = "NixEko.Ability.Glider.v1";

        /// <summary>Can Nix send Eko out to recall a downed arrow (R2 with no arrow in hand)?</summary>
        public static bool RecallArrow
        {
            get => GetBool(RecallArrowKey, true);
            set => SetBool(RecallArrowKey, value);
        }

        /// <summary>Can Nix summon the shade (Eko phantom) at all? Turning this off suppresses
        /// the L1 possession beat entirely — the shade can't be made, so it also can't be
        /// planted or fired.</summary>
        public static bool MakeShade
        {
            get => GetBool(MakeShadeKey, true);
            set => SetBool(MakeShadeKey, value);
        }

        /// <summary>Can the shade actually fire an arrow? Gates both the R2 fire-during-
        /// possession shortcut and the L1 fire-planted-phantom beat: with the flag off the shade
        /// still summons and can be planted, but every "shoot" path collapses to a plain return
        /// (phantom orbs home without loosing).</summary>
        public static bool ShadeFireArrow
        {
            get => GetBool(ShadeFireArrowKey, true);
            set => SetBool(ShadeFireArrowKey, value);
        }

        /// <summary>Is the glider unlocked? With this off, <see cref="Player.PlayerController.IsGliding"/>
        /// always returns false regardless of held input or fuel, so holding L2 in the air just
        /// falls normally.</summary>
        public static bool Glider
        {
            get => GetBool(GliderKey, true);
            set => SetBool(GliderKey, value);
        }

        static bool GetBool(string key, bool defaultValue)
            => PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;

        static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
