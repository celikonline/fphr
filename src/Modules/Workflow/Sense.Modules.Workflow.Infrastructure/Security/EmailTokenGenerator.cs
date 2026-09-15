using System.Security.Cryptography;
using System.Text;
using Sense.Modules.Workflow.Application;

namespace Sense.Modules.Workflow.Infrastructure.Security;

/// <summary>Cryptographically random token (base64url) and its SHA-256 hash (hex) - mirrors Identity's ISecretGenerator/SecretGenerator.</summary>
public sealed class EmailTokenGenerator : IEmailTokenGenerator
{
    public string NewToken(int bytes = 32)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);

        // Base64url (RFC 4648 §5), same encoding Identity's SecretGenerator gets from Microsoft.IdentityModel.Tokens'
        // Base64UrlEncoder - reproduced by hand here so Workflow.Infrastructure doesn't need that package.
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
