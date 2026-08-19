namespace NixAndEko.Combat
{
    /// <summary>Implemented by anything an arrow can interact with (switches, breakables, enemies).</summary>
    public interface IArrowHittable
    {
        /// <param name="arrow">The arrow that struck this object.</param>
        /// <returns>True if the arrow should stick, false if it should be consumed/destroyed.</returns>
        bool OnArrowHit(Arrow arrow);
    }
}
