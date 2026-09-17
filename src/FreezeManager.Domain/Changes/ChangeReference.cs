using System.Globalization;

namespace FreezeManager.Domain.Changes;

/// <summary>A human-quotable change identifier, e.g. <c>CHG-2026-0001</c>.</summary>
public readonly record struct ChangeReference
{
    public const string Prefix = "CHG";

    private ChangeReference(int year, int sequence)
    {
        Year = year;
        Sequence = sequence;
    }

    public int Year { get; }

    public int Sequence { get; }

    public static ChangeReference Create(int year, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2000);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);

        return new ChangeReference(year, sequence);
    }

    public static bool TryParse(string? value, out ChangeReference reference)
    {
        reference = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('-');

        if (parts.Length != 3
            || !parts[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || year < 2000
            || sequence < 1)
        {
            return false;
        }

        reference = new ChangeReference(year, sequence);
        return true;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}-{Year:D4}-{Sequence:D4}");
}
