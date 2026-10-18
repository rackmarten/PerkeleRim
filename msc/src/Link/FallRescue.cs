namespace PerkeleRim
{
    // Where CatchFall puts a player that has no island surface under its feet but some above, and where
    // a vehicle that went through the island is put back.
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

        // A vehicle that last stood on its wheels this far (meters) or closer is put back there.
        public const float VehicleReach = 60f;
        // With nothing remembered, only a surface this close above the vehicle's origin is the ground
        // it sank through (its wheels hang about a meter under the origin); further up is a cliff or a
        // roof, and putting the vehicle on that dropped it, and the player in it, from a height.
        public const float VehicleMaxAbove = 4f;

        public enum Pick { Leave, Stood, Above }

        // haveStood: the vehicle stood on its wheels at some point; stoodDistance: from there to where
        // it is now; carY: its origin's height; lowestAbove: the lowest island surface above it.
        public static Pick Vehicle(bool haveStood, float stoodDistance, float carY, float lowestAbove)
        {
            if (haveStood && stoodDistance <= VehicleReach) return Pick.Stood;
            if (!float.IsNaN(lowestAbove) && lowestAbove - carY <= VehicleMaxAbove) return Pick.Above;
            return Pick.Leave;
        }
    }
}
