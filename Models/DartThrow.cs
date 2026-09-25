namespace J1sDartSharp.Models;

/// <summary>
/// Represents one dart thrown in a trip to the oche.
/// Number = board segment (0=miss, 1-20, 25=bull).
/// Multiplier = 1 single / 2 double / 3 triple (bull max = 2).
/// Score = Number * Multiplier.
/// </summary>
public readonly record struct DartThrow(int Number, int Multiplier)
{
    public int Score => Number * Multiplier;

    public override string ToString() => Number == 0
        ? "Miss"
        : Multiplier switch { 2 => $"D{Number}", 3 => $"T{Number}", _ => $"{Number}" };
}
