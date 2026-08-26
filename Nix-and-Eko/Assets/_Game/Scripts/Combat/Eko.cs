using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Eko: a faerie (they/them) who becomes directly player-controlled the instant Nix summons
    /// them (L1). Lives on the same GameObject as the phantom's own <see cref="PlayerController"/>
    /// — this component only owns the phantom's lifecycle and look (tint, spawn/return effects);
    /// all of the actual walking/jumping/falling is the same locomotion code Nix uses.
    ///
    /// While Eko is out, Nix stands frozen wherever she was — her own <see cref="PlayerController"/>
    /// keeps ticking (so she still falls if summoned mid-air), just fed no input. See
    /// <see cref="EkoSummoner"/> for the control hand-off, the arrow-grab, and how Eko gets back:
    /// pressing the Nix Bow button while possessed doesn't fire (Eko has no bow) — it tries to
    /// send control home instead, cleanly if Nix is grounded, or as a "vanish" (Eko yanked out of
    /// the world on the spot) if she isn't.
    /// </summary>
    public class Eko : MonoBehaviour
    {
        [Header("References")]
        public SpriteRenderer sprite;
        [Tooltip("Nix — the orb-return home target.")]
        public PlayerController player;

        // A code constant (not serialized) so recompiles always apply — a serialized colour is
        // baked into the scene's Eko at build time and would ignore edits until a scene rebuild.
        static readonly Color tint = new Color(0.4f, 0.8f, 1f, 0.85f);
        // Draw the echo in front of Nix (her sprite is 10) so it's always visible, even planted
        // right on top of her the instant they swap control.
        const int PhantomSortingOrder = 11;

        /// <summary>True while the phantom is out in the world (under player control).</summary>
        public bool Active { get; private set; }

        /// <summary>
        /// Plant the phantom at <paramref name="position"/>, facing <paramref name="facing"/>, and
        /// switch it on. <see cref="EkoSummoner"/> takes care of actually handing control over —
        /// this just handles the visual side of the swap.
        /// </summary>
        public void Summon(Vector3 position, int facing)
        {
            transform.position = position;

            if (sprite != null)
            {
                // Force the visual state in code every summon, so a scene whose Eko was baked with
                // stale values (sorting order, hidden renderer, missing sprite) still shows.
                sprite.enabled = true;
                sprite.sortingOrder = PhantomSortingOrder;
                if (sprite.sprite == null) sprite.sprite = ArcherSprites.IdleFrames[0];
                sprite.color = tint;

                float mag = Mathf.Abs(sprite.transform.localScale.x);
                if (mag < 0.01f) mag = 1f;
                sprite.transform.localScale = new Vector3(mag * (facing >= 0 ? 1 : -1), 1f, 1f);
            }

            Active = true;
            gameObject.SetActive(true);
            Sfx.Play(Sfx.Id.EkoSpawn);
        }

        void Update()
        {
            if (!Active || sprite == null) return;
            if (sprite.color != tint) sprite.color = tint;
        }

        /// <summary>Send the phantom away outright — no travel effect. Used once whatever effect
        /// is going to play (orb / vanish burst) has already fired.</summary>
        public void Dismiss()
        {
            Active = false;
            gameObject.SetActive(false);
        }

        /// <summary>Collapse into a blue orb that zips home to Nix, then dismiss — the clean
        /// return: Nix was grounded, so Eko made it home properly.</summary>
        public void DismissWithOrb()
        {
            if (!Active) return;
            Vector3 from = transform.position;
            Vector3 to = player != null ? player.transform.position : from;
            EkoOrb.Fly(from, to, 0.2f);
            Dismiss();
        }

        /// <summary>Eko yanked out of the world on the spot — the player tried to send them home
        /// (Nix Bow button) while Nix wasn't grounded to receive them. A burst where they stood
        /// instead of a clean flight home.</summary>
        public void Vanish()
        {
            if (!Active) return;
            Particle.Burst(transform.position, tint, 16, 6f, 0.4f, 0.8f);
            Sfx.Play(Sfx.Id.EkoZip, 0.75f);
            Dismiss();
        }
    }
}
