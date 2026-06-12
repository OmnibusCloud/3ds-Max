using System.Security.Cryptography;
using System.Text;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

[TestFixture]
public sealed class MaxPkceUtilsTests
{
    #region Tests

    [Test]
    public void GenerateCodeVerifierProducesUniqueUrlSafeValuesTest()
    {
        var first = MaxPkceUtils.GenerateCodeVerifier();
        var second = MaxPkceUtils.GenerateCodeVerifier();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first, Does.Not.Contain("+").And.Not.Contain("/").And.Not.Contain("="));
            Assert.That(first.Length, Is.GreaterThanOrEqualTo(43), "RFC 7636 requires a 43..128 character verifier.");
            Assert.That(first.Length, Is.LessThanOrEqualTo(128));
        });
    }

    [Test]
    public void ComputeCodeChallengeMatchesRfc7636S256Test()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        var challenge = MaxPkceUtils.ComputeCodeChallenge(verifier);

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        Assert.That(challenge, Is.EqualTo(expected));
    }

    #endregion
}
