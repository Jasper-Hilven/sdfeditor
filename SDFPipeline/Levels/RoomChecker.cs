namespace SDFPipeline.Levels;

/// <summary>The six faces of a room (see <see cref="RoomSpec"/>), named by the side they stand on.</summary>
public enum RoomFace
{
    Floor,
    Ceiling,
    /// <summary>The wall at x = -width/2.</summary>
    NegX,
    /// <summary>The wall at x = +width/2.</summary>
    PosX,
    /// <summary>The wall at z = -depth/2.</summary>
    NegZ,
    /// <summary>The wall at z = +depth/2.</summary>
    PosZ,
}

/// <summary>
/// The colour of every square of a room's six chessboards: one checker pattern over the surface of the cube.
/// <para>
/// A square is (face, u, v). On the floor and ceiling u counts squares along x from the -x side and v along z from the -z side. On the
/// walls at -z and +z, u counts along x from the -x side; on the walls at -x and +x, u counts along z from the -z side; v is the row from
/// the floor up (0 is the bottom row).
/// </para>
/// <para>
/// The floor is the plain board, the corner (0, 0) dark. Each wall continues it as if folded up from the floor's edge, and the ceiling
/// continues the -z wall, so the colour alternates across the floor's four edges and the ceiling's four edges. It cannot alternate
/// across all twelve: three squares meet at each of the cube's eight corners, each next to the other two, and no two colours colour a
/// triangle. The unavoidable repeats are put on the four vertical wall-to-wall edges, where the two neighbouring squares have the same
/// colour all along; that is the least possible, as every corner needs a repeat on one of its three edges and one edge serves two corners.
/// </para>
/// </summary>
public static class RoomChecker
{
    /// <param name="squares">How many squares the room has along x, y (height) and z.</param>
    public static bool IsDark(RoomFace face, int u, int v, (int X, int Y, int Z) squares)
    {
        // The parity of the square on the floor's pattern continued over the fold: a wall's row v lies at floor row -1 - v beyond the
        // -z (or -x) edge, or at squares + v beyond the +z (or +x) edge; the ceiling continues the -z wall upwards
        var parity = face switch
        {
            RoomFace.Floor => u + v,
            RoomFace.NegZ => u + v + 1,
            RoomFace.PosZ => u + squares.Z + v,
            RoomFace.NegX => u + v + 1,
            RoomFace.PosX => u + squares.X + v,
            RoomFace.Ceiling => u + v + squares.Y + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };
        return parity % 2 == 0;
    }

    /// <summary>The number of squares along u and along v on a face.</summary>
    public static (int U, int V) Size(RoomFace face, (int X, int Y, int Z) squares) => face switch
    {
        RoomFace.Floor or RoomFace.Ceiling => (squares.X, squares.Z),
        RoomFace.NegZ or RoomFace.PosZ => (squares.X, squares.Y),
        _ => (squares.Z, squares.Y),
    };
}
