using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using ContentManagementSystem.Core.Preview;

namespace ContentManagementSystem.Core.Tests.Preview;

/// <summary>
/// The secret a shareable preview link is made of (task P3-17, spec section 12.2).
/// </summary>
/// <remarks>
/// Everything here is about one property: <strong>the issuing path and the redeeming path must agree
/// about what is hashed.</strong> They cannot be reconciled after the fact, because the token is
/// stored nowhere — a mismatch produces links that are issued successfully and never work, with
/// nothing left to compare against while debugging it.
/// </remarks>
public class PreviewTokensTests
{
    [Test]
    public void ATokenIsThirtyTwoBytesOfEntropyEncodedAsBase64Url()
    {
        var (token, hash) = PreviewTokens.Create();

        // base64url, so it survives being pasted into a URL, an email client, and a chat window
        // without any of them escaping it into something else.
        token.Should().NotContain("+").And.NotContain("/").And.NotContain("=");

        Base64Url.DecodeFromChars(token).Should().HaveCount(PreviewTokens.TokenBytes);
        hash.Should().HaveCount(32);
    }

    [Test]
    public void TwoTokensAreNeverTheSame()
    {
        // Not a real entropy test — that is the CSPRNG's job — but it does catch the one mistake
        // that would be catastrophic and easy: a static or seeded generator.
        var tokens = Enumerable.Range(0, 256).Select(_ => PreviewTokens.Create().Token).ToList();

        tokens.Distinct().Should().HaveCount(tokens.Count);
    }

    [Test]
    public void APresentedTokenHashesToWhatIssuingStored()
    {
        var (token, stored) = PreviewTokens.Create();

        PreviewTokens.TryHash(token, out var presented).Should().BeTrue();
        presented.Should().Equal(stored);
    }

    [Test]
    public void TheHashIsTakenOverTheDecodedBytesNotTheEncodedString()
    {
        // The distinction that would otherwise be discovered in production. base64url has spellings
        // that differ as text and decode identically, so hashing the string would make one secret
        // hash to several values and the lookup would depend on which spelling reached the server.
        var (token, stored) = PreviewTokens.Create();

        stored.Should().Equal(SHA256.HashData(Base64Url.DecodeFromChars(token)));
        stored.Should().NotEqual(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not a token")]
    [Arguments("c2hvcnQ")]
    [Arguments("bG9uZ2VyLXRoYW4tdGhpcnR5LXR3by1ieXRlcy1vZi1kZWNvZGVkLW1hdGVyaWFs")]
    public void SomethingThatWasNeverIssuedIsRefusedWithoutAQuery(string? candidate)
    {
        // Shape is checked before the database is asked, which is the cheap half of the rate limit:
        // a crawler walking /preview/s/{anything} is answered without touching SQL.
        PreviewTokens.TryHash(candidate, out var hash).Should().BeFalse();
        hash.Should().BeEmpty();
    }

    [Test]
    public void TheSharedUrlIsAssembledInOnePlace()
    {
        // So that no caller has to know the shape of the path and get it subtly wrong — a link with
        // the wrong prefix is one nobody can debug from the row, because the row holds a hash.
        var (token, _) = PreviewTokens.Create();

        PreviewTokens.UrlFor(token).Should().Be($"/preview/s/{token}");
    }

    [Test]
    public void TheExpiryBoundsAreTheOnesTheSpecStates()
    {
        // Pinned rather than left implicit: seven days and thirty are numbers in spec section 12.2,
        // and a change to either is a change to how long unpublished content stays readable.
        PreviewTokens.DefaultExpiryDays.Should().Be(7);
        PreviewTokens.MaxExpiryDays.Should().Be(30);
    }
}
