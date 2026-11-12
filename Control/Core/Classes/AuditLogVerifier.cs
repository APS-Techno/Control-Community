using System.Text.Json;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Offline verifier for the hash-chained audit log. Walks a file top to
/// bottom and reports the first record whose computed hash does not
/// match, whose Prev does not match the previous record's Hash, whose
/// sequence number is out of order, or that fails to parse.
///
/// Every audit-integrity claim in the compliance-positioning doc rests
/// on this being rerunnable without the live system — therefore this
/// class has no dependencies on <see cref="OverrideRegistry"/>,
/// <see cref="FileAuditLogWriter"/>'s instance state, or the clock.
/// </summary>
public static class AuditLogVerifier
{
    public readonly record struct VerifyResult(
        bool Ok,
        long RecordsChecked,
        long? BrokenAt,
        string? Reason)
    {
        public static VerifyResult Success(long records)
            => new(true, records, null, null);

        public static VerifyResult Fail(long brokenAt, string reason)
            => new(false, brokenAt, brokenAt, reason);
    }

    /// <summary>
    /// Verifies a single <c>.jsonl</c> audit file assuming the first record
    /// chains from <paramref name="initialPrev"/>. Pass
    /// <see cref="FileAuditLogWriter.ChainSeed"/> to verify a stand-alone file,
    /// or the last hash of the prior day's file to verify a multi-day chain.
    /// </summary>
    public static VerifyResult VerifyFile(string path, string initialPrev)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            return VerifyResult.Fail(0, $"file not found: {path}");

        using var reader = new StreamReader(path);
        return VerifyChain(EnumerateLines(reader), initialPrev);
    }

    /// <summary>
    /// Reports the first break. Stream-based so very long files don't buffer
    /// entirely in memory.
    /// </summary>
    public static VerifyResult VerifyChain(IEnumerable<string> lines, string initialPrev)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string expectedPrev = initialPrev;
        long expectedSeq = 1;
        long lineNumber = 0;
        var json = new JsonSerializerOptions { WriteIndented = false };

        foreach (string raw in lines)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(raw)) continue;

            AuditEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<AuditEvent>(raw, json);
            }
            catch (JsonException ex)
            {
                return VerifyResult.Fail(lineNumber, $"JSON parse failed: {ex.Message}");
            }
            if (evt is null)
                return VerifyResult.Fail(lineNumber, "JSON parsed to null");

            if (evt.Seq != expectedSeq)
                return VerifyResult.Fail(lineNumber,
                    $"sequence mismatch: expected {expectedSeq}, got {evt.Seq}");

            if (!string.Equals(evt.Prev, expectedPrev, StringComparison.OrdinalIgnoreCase))
                return VerifyResult.Fail(lineNumber,
                    $"prev mismatch: expected {expectedPrev}, got {evt.Prev}");

            string recomputed = FileAuditLogWriter.ComputeHash(evt, json);
            if (!string.Equals(recomputed, evt.Hash, StringComparison.OrdinalIgnoreCase))
                return VerifyResult.Fail(lineNumber,
                    $"hash mismatch: expected {recomputed}, got {evt.Hash}");

            expectedPrev = evt.Hash;
            expectedSeq++;
        }

        return VerifyResult.Success(expectedSeq - 1);
    }

    private static IEnumerable<string> EnumerateLines(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
