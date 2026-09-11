using System.Security.Cryptography;
using System.Text;
using Shared.Common.Abstractions;

namespace Shared.Common.Implementations;

public class Sha256HashGenerator : IHashGenerator
{
    public string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
