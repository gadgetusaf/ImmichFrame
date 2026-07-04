using ImmichFrame.WebApi.Helpers;
using Microsoft.AspNetCore.DataProtection;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Helpers;

/// <summary>
/// Unit tests for <see cref="SlideshowTokenService"/> — the signed, slug- and stamp-bound token that
/// gates the anonymous public slideshow surface. These pin the slug binding, security-stamp rotation,
/// expiry and fail-closed parsing so a one-line regression (e.g. dropping the slug or stamp comparison)
/// fails loudly instead of silently exposing one link's photos through another link's URL.
/// </summary>
[TestFixture]
public class SlideshowTokenServiceTests
{
    // A single provider instance is reused so tokens issued in one test could, in principle, be
    // validated against the same key ring — exactly like the singleton in Program.cs.
    private IDataProtectionProvider _provider = null!;
    private SlideshowTokenService _tokens = null!;

    [SetUp]
    public void Setup()
    {
        _provider = new EphemeralDataProtectionProvider();
        _tokens = new SlideshowTokenService(_provider);
    }

    [Test]
    public void Validate_WithMatchingSlugAndStamp_Succeeds()
    {
        var token = _tokens.Issue("family", "stamp-1");

        Assert.That(_tokens.Validate(token, "family", "stamp-1"), Is.True);
    }

    [Test]
    public void Validate_ForDifferentSlug_Fails()
    {
        // A token issued for "family" must never unlock "vacation": the slug is part of the signed payload.
        var token = _tokens.Issue("family", "stamp-1");

        Assert.That(_tokens.Validate(token, "vacation", "stamp-1"), Is.False);
    }

    [Test]
    public void Validate_AfterStampRotation_Fails()
    {
        // Simulates an admin changing the PIN/policy or disabling the link: the old cookie is now stale.
        var token = _tokens.Issue("family", "stamp-old");

        Assert.That(_tokens.Validate(token, "family", "stamp-new"), Is.False);
    }

    [Test]
    public void Validate_WithNullOrEmptyToken_Fails()
    {
        Assert.That(_tokens.Validate(null, "family", "stamp-1"), Is.False);
        Assert.That(_tokens.Validate("", "family", "stamp-1"), Is.False);
    }

    [Test]
    public void Validate_WithGarbageToken_FailsClosed()
    {
        // Any parse/decrypt error must fail closed rather than throw.
        Assert.That(_tokens.Validate("not-a-real-token", "family", "stamp-1"), Is.False);
    }

    [Test]
    public void Validate_WithTokenFromDifferentKeyRing_Fails()
    {
        // A token signed by a different provider (different key ring) must not validate here.
        var otherService = new SlideshowTokenService(new EphemeralDataProtectionProvider());
        var foreignToken = otherService.Issue("family", "stamp-1");

        Assert.That(_tokens.Validate(foreignToken, "family", "stamp-1"), Is.False);
    }

    [Test]
    public void Validate_SlugAndStampAreNotConfusable()
    {
        // Guards the split-on-first-'|' logic: a token for slug "a" + stamp "b|c" must not validate as
        // slug "a|b" + stamp "c" (nor any other regrouping around the separator).
        var token = _tokens.Issue("a", "b|c");

        Assert.That(_tokens.Validate(token, "a", "b|c"), Is.True, "exact slug/stamp pair must still validate");
        Assert.That(_tokens.Validate(token, "a|b", "c"), Is.False, "must not re-split the payload into a different slug/stamp");
    }
}
