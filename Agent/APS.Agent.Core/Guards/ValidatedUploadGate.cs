using System.Security.Cryptography;
using System.Text;

namespace APS.Agent.Core.Guards;

public sealed class ValidatedUploadGate
{
    private readonly Dictionary<string, bool> _validatedDigests = new(StringComparer.Ordinal);

    public static string ComputeDigest(string content)
    {
        string normalized = (content ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public void RecordValidationResult(string content, bool valid)
    {
        _validatedDigests[ComputeDigest(content)] = valid;
    }

    public (bool Allowed, string? ErrorCode, string? Message) CanUpload(string content, bool requireValidatedUpload)
    {
        if (!requireValidatedUpload)
            return (true, null, null);

        string digest = ComputeDigest(content);
        if (_validatedDigests.TryGetValue(digest, out bool valid) && valid)
            return (true, null, null);

        return (
            false,
            "validation_required_before_upload",
            "Run validate_recipe and confirm valid=true on this exact content before calling upload_recipe.");
    }
}
