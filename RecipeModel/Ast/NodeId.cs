using System.Security.Cryptography;
using System.Text;

namespace APS.RecipeModel.Ast;

public readonly record struct NodeId(string Value)
{
    public static NodeId FromPosition(string kind, string parentPath, int indexInParent)
    {
        var raw = $"{parentPath}/{kind}[{indexInParent}]";
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(Encoding.UTF8.GetBytes(raw), hash);
        // First 8 hex chars is plenty of uniqueness for a recipe.
        return new NodeId(Convert.ToHexString(hash[..4]).ToLowerInvariant());
    }

    public override string ToString() => Value;
}
