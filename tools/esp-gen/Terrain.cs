using Mutagen.Bethesda.Skyrim;

namespace PerkeleRim.EspGen;

// Terrain height of a worldspace from its LAND records (VHGT): per cell 33 x 33 points 128 units
// apart, stored as a start height plus per-point deltas, all in steps of 8 units.
sealed class Terrain
{
    const float CellUnits = 4096f;
    const float PointUnits = 128f;

    readonly Dictionary<(int X, int Y), float[,]> cells = new();

    public Terrain(IWorldspaceGetter world)
    {
        foreach (var cell in world.SubCells.SelectMany(b => b.Items).SelectMany(s => s.Items))
        {
            var map = cell.Landscape?.VertexHeightMap;
            if (map == null || cell.Grid == null) continue;
            var h = new float[33, 33];
            float rowStart = map.Offset;
            for (int r = 0; r < 33; r++)
            {
                rowStart += (sbyte)map.HeightMap[0, r];
                float at = rowStart;
                h[r, 0] = at * 8f;
                for (int c = 1; c < 33; c++)
                {
                    at += (sbyte)map.HeightMap[c, r];
                    h[r, c] = at * 8f;
                }
            }
            cells[(cell.Grid.Point.X, cell.Grid.Point.Y)] = h;
        }
    }

    // Bilinear between the four surrounding points.
    public float HeightAt(float x, float y)
    {
        int cx = (int)MathF.Floor(x / CellUnits), cy = (int)MathF.Floor(y / CellUnits);
        if (!cells.TryGetValue((cx, cy), out var h)) throw new InvalidOperationException($"no terrain for cell {cx},{cy}");
        float fx = (x - cx * CellUnits) / PointUnits, fy = (y - cy * CellUnits) / PointUnits;
        int c = Math.Min((int)fx, 31), r = Math.Min((int)fy, 31);
        float tx = fx - c, ty = fy - r;
        return h[r, c] * (1 - tx) * (1 - ty) + h[r, c + 1] * tx * (1 - ty) + h[r + 1, c] * (1 - tx) * ty + h[r + 1, c + 1] * tx * ty;
    }
}
