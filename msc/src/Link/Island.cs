namespace PerkeleRim
{
    // Maps between Skyrim world space (the wire) and the collision island MSC builds far away from
    // its own map. Skyrim sends an anchor (WorldspaceChange.spawn); that point sits at Base on the
    // island, and everything else is offset from it. Offsets are taken in Skyrim units before
    // converting, so large Skyrim coordinates don't cost float precision on the island.
    // Unity-free; convert Vec3 to Vector3 at the call site.
    public sealed class Island
    {
        // Island origin in Unity meters: well outside MSC's map, and high enough that nothing in
        // Skyrim lands below the y < -5.85 the game treats as being under water (vehicles' AirIntake
        // drowns the engine below -4.5). Skyrim spans about 900 m from the sea to the Throat of the
        // World, so even an anchor on the peak keeps the coast above it; at 100 the van died on a
        // road 120 m below its anchor.
        public static readonly Vec3 Base = new Vec3(10000f, 1000f, 10000f);

        public bool HasAnchor { get; private set; }
        public uint Generation { get; private set; }
        public uint Worldspace { get; private set; }
        public uint Cell { get; private set; }
        public Vec3 Anchor { get; private set; }  // Skyrim units
        public float AnchorHeading { get; private set; }  // Skyrim radians

        public void SetAnchor(uint generation, uint worldspace, uint cell, Vec3 anchor, float heading)
        {
            HasAnchor = true;
            Generation = generation;
            Worldspace = worldspace;
            Cell = cell;
            Anchor = anchor;
            AnchorHeading = heading;
        }

        public Vec3 SkyrimToIsland(Vec3 skyrim)
        {
            var u = Coords.SkyrimToUnity(new Vec3(skyrim.x - Anchor.x, skyrim.y - Anchor.y, skyrim.z - Anchor.z));
            return new Vec3(Base.x + u.x, Base.y + u.y, Base.z + u.z);
        }

        public Vec3 IslandToSkyrim(Vec3 unity)
        {
            var s = Coords.UnityToSkyrim(new Vec3(unity.x - Base.x, unity.y - Base.y, unity.z - Base.z));
            return new Vec3(Anchor.x + s.x, Anchor.y + s.y, Anchor.z + s.z);
        }
    }

    // PlayerState.stance values.
    public static class Stance
    {
        public const byte Standing = 0;
        public const byte Crouching = 1;
        public const byte Prone = 2;

        // From the active state of MSC's PLAYER :: Crouch FSM (docs/FSM_MAP.md).
        public static byte FromCrouchState(string state)
        {
            switch (state)
            {
                case "Move down 1":
                case "Crouched 1":
                    return Crouching;
                case "Move down 2":
                case "Crouched 2":
                    return Prone;
                default:
                    return Standing;
            }
        }
    }
}
