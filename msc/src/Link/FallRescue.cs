namespace PerkeleRim
{
    // Where CatchFall puts a player that has no island surface under its feet but some above.
    public static class FallRescue
    {
        // On foot with nothing remembered, only a surface this close above the feet is the floor
        // fallen through; further up is a ceiling or a room's shell (a seam between room pieces).
        public const float MaxAbove = 1f;

        // Returns the height to put the player at, or float.NaN to leave it alone. haveGrounded:
        // the player stood on foot within reach recently (groundedY); lowestAbove: the lowest
        // island surface above the feet.
        public static float Target(bool onFoot, bool haveGrounded, float groundedY, float feetY, float lowestAbove)
        {
            if (!onFoot) return lowestAbove;
            if (haveGrounded) return groundedY;
            return lowestAbove - feetY <= MaxAbove ? lowestAbove : float.NaN;
        }
    }
}
