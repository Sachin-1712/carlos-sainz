using FreezeManager.Domain.Audit;
using Xunit;

namespace FreezeManager.Domain.Tests.Audit;

public class AuditChainTests
{
    private static readonly DateTimeOffset T = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    private static AuditEntry Entry(long sequence, string previousHash, string subject = "CHG-2026-0001",
        AuditAction action = AuditAction.ChangeRaised, string actor = "s.sindhe", string details = "{}") =>
        new(sequence, T.AddMinutes(sequence), actor, action, subject, details, previousHash);

    private static List<AuditEntry> Chain(int length)
    {
        var entries = new List<AuditEntry>();
        var previous = AuditEntry.GenesisHash;

        for (var i = 1; i <= length; i++)
        {
            var entry = Entry(i, previous);
            entries.Add(entry);
            previous = entry.Hash;
        }

        return entries;
    }

    [Fact]
    public void An_empty_log_is_trivially_intact()
    {
        var result = AuditChain.Verify(Array.Empty<AuditEntry>());

        Assert.True(result.IsValid);
        Assert.Equal(0, result.EntryCount);
    }

    [Fact]
    public void A_well_formed_chain_verifies()
    {
        var result = AuditChain.Verify(Chain(25));

        Assert.True(result.IsValid);
        Assert.Equal(25, result.EntryCount);
        Assert.Null(result.FirstBrokenSequence);
    }

    [Fact]
    public void The_first_entry_must_link_to_the_genesis_hash()
    {
        var entries = new[] { Entry(1, new string('a', 64)) };

        var result = AuditChain.Verify(entries);

        Assert.False(result.IsValid);
        Assert.Equal(1, result.FirstBrokenSequence);
        Assert.Contains("genesis", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Editing_an_entry_breaks_it_because_the_hash_no_longer_matches_its_contents()
    {
        var entries = Chain(5);

        // Rebuild entry 3 with different details but keep the original hashes around it, as an
        // attacker editing the database row would.
        var original = entries[2];
        var tampered = new AuditEntry(
            original.Sequence, original.OccurredAtUtc, original.Actor, original.Action,
            original.Subject, """{"amount":"changed"}""", original.PreviousHash);

        Assert.NotEqual(original.Hash, tampered.Hash);
    }

    [Fact]
    public void Removing_an_entry_breaks_the_sequence()
    {
        var entries = Chain(5);
        entries.RemoveAt(2);

        var result = AuditChain.Verify(entries);

        Assert.False(result.IsValid);
        Assert.Equal(4, result.FirstBrokenSequence);
        Assert.Contains("removed or reordered", result.Reason!);
    }

    [Fact]
    public void Truncating_the_end_of_the_log_still_verifies_which_is_why_the_count_is_reported()
    {
        // A hash chain cannot detect that entries were removed from the end -- nothing links
        // forward. The count is reported so a caller comparing against an expected high-water mark
        // can notice. This test documents the limit rather than pretending it does not exist.
        var entries = Chain(5);
        entries.RemoveRange(3, 2);

        var result = AuditChain.Verify(entries);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.EntryCount);
    }

    [Fact]
    public void Reordering_two_entries_is_detected()
    {
        var entries = Chain(5);
        (entries[1], entries[3]) = (entries[3], entries[1]);

        // Verify sorts by sequence, so the reorder only matters if sequences were also swapped.
        // Rebuild entry 2 carrying entry 4's contents, which is the realistic attack.
        var fourth = entries[3];
        entries[1] = new AuditEntry(
            2, fourth.OccurredAtUtc, fourth.Actor, fourth.Action, fourth.Subject,
            fourth.Details, fourth.PreviousHash);

        var result = AuditChain.Verify(entries);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_relinked_entry_is_detected_even_when_its_own_hash_is_recomputed()
    {
        var entries = Chain(5);

        // Recompute entry 3 honestly, but pointing at the wrong predecessor.
        var third = entries[2];
        entries[2] = new AuditEntry(
            third.Sequence, third.OccurredAtUtc, third.Actor, third.Action,
            third.Subject, third.Details, entries[0].Hash);

        var result = AuditChain.Verify(entries);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.FirstBrokenSequence);
        Assert.Contains("does not link", result.Reason!);
    }

    [Fact]
    public void Rewriting_an_entry_and_honestly_rehashing_it_is_caught_by_the_next_entry()
    {
        // The thorough attack: edit entry 7's actor and recompute entry 7's own hash so it is
        // internally consistent. That entry now verifies on its own -- but entry 8 still carries a
        // back-link to the hash entry 7 used to have, so the forgery surfaces one entry later.
        // Undoing that would mean rewriting every entry from 7 to the end.
        var entries = Chain(10);
        var seventh = entries[6];

        entries[6] = new AuditEntry(
            seventh.Sequence, seventh.OccurredAtUtc, "someone.else", seventh.Action,
            seventh.Subject, seventh.Details, seventh.PreviousHash);

        var result = AuditChain.Verify(entries);

        Assert.False(result.IsValid);
        Assert.Equal(8, result.FirstBrokenSequence);
        Assert.Contains("does not link", result.Reason!);

        // Entries 1 to 6 are untouched and remain trustworthy.
        Assert.True(AuditChain.Verify(entries.Take(6)).IsValid);
        Assert.Equal(10, result.EntryCount);
    }

    [Fact]
    public void Field_boundaries_cannot_be_shifted_to_forge_a_matching_hash()
    {
        // Length-prefixed fields: moving text across a boundary must change the hash. With a plain
        // separator these two would hash identically, and a forgery would need no key at all.
        var left = AuditEntry.ComputeHash(1, T, "alice", AuditAction.ChangeRaised, "CHG-1", "{}", AuditEntry.GenesisHash);
        var right = AuditEntry.ComputeHash(1, T, "alice;CHG-1", AuditAction.ChangeRaised, "", "{}", AuditEntry.GenesisHash);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void The_same_contents_always_hash_the_same_way()
    {
        var first = AuditEntry.ComputeHash(3, T, "a", AuditAction.OverrideGranted, "CHG-1", """{"x":1}""", AuditEntry.GenesisHash);
        var second = AuditEntry.ComputeHash(3, T, "a", AuditAction.OverrideGranted, "CHG-1", """{"x":1}""", AuditEntry.GenesisHash);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void An_entry_requires_an_actor_and_a_subject()
    {
        Assert.Throws<ArgumentException>(() => Entry(1, AuditEntry.GenesisHash, actor: "  "));
        Assert.Throws<ArgumentException>(() => Entry(1, AuditEntry.GenesisHash, subject: "  "));
    }
}
