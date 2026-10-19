namespace Sideclip.History;

public enum ClipKind
{
    Text,
    Image
}

/// <summary>How many clips a cap change removed (per kind).</summary>
public readonly record struct CapRemoval(int Text, int Images)
{
    public int Total => Text + Images;
}

/// <summary>
/// Pure rules for the per-kind history caps. No I/O. The store is the only
/// caller that turns a plan into removals, so there is one enforcement path.
///
/// Definitions:
///  - A cap of 0 (or less) means Unlimited.
///  - Only non-secret, non-pinned clips with a payload count toward a cap.
///  - "Oldest" is the end of the store's newest-first sequence. The store only
///    ever inserts new clips at index 0, so sequence order equals capture
///    order and cannot tie. (CopiedAt is display only: profiles written by
///    older builds have CopiedAt reset on every launch, so it cannot be trusted
///    for ordering.)
/// </summary>
internal static class ClipCaps
{
    public const int Unlimited = 0;

    public static bool IsUnlimited(int cap) => cap <= 0;

    public static ClipKind KindOf(ClipEntry e) => e.IsImage ? ClipKind.Image : ClipKind.Text;

    public static bool Counts(ClipEntry e, ClipKind kind) =>
        !e.IsSecret && !e.IsPinned && e.HasPayload && KindOf(e) == kind;

    public static int CountOf(IEnumerable<ClipEntry> entries, ClipKind kind)
    {
        var n = 0;
        foreach (var e in entries)
        {
            if (Counts(e, kind))
                n++;
        }

        return n;
    }

    /// <summary>
    /// Entries that must go to bring <paramref name="kind"/> down to
    /// <paramref name="cap"/>, oldest first, never more than
    /// <paramref name="maxRemovals"/>. Never selects pinned, secret or
    /// other-kind entries. Does not modify anything.
    /// </summary>
    public static List<ClipEntry> SelectOldestToRemove(
        IReadOnlyList<ClipEntry> newestFirst,
        ClipKind kind,
        int cap,
        int maxRemovals)
    {
        var result = new List<ClipEntry>();
        if (IsUnlimited(cap) || maxRemovals <= 0)
            return result;

        var excess = CountOf(newestFirst, kind) - cap;
        var take = Math.Min(excess, maxRemovals);
        if (take <= 0)
            return result;

        for (var i = newestFirst.Count - 1; i >= 0 && result.Count < take; i--)
        {
            if (Counts(newestFirst[i], kind))
                result.Add(newestFirst[i]);
        }

        return result;
    }

    /// <summary>
    /// How many clips the user must confirm removing when OK is pressed with the
    /// cap going from <paramref name="oldCap"/> to <paramref name="newCap"/> and
    /// <paramref name="stored"/> countable clips stored. Choosing Unlimited, or
    /// raising a bounded cap, removes nothing. Keeping a cap that the stored
    /// clips already exceed (an over-cap profile) or lowering it asks for
    /// stored - newCap, so an over-cap profile is only ever trimmed after an
    /// explicit confirmation.
    /// </summary>
    public static int PlanLowering(int oldCap, int newCap, int stored)
    {
        if (IsUnlimited(newCap))
            return 0;
        if (!IsUnlimited(oldCap) && newCap > oldCap)
            return 0;
        return Math.Max(0, stored - newCap);
    }
}