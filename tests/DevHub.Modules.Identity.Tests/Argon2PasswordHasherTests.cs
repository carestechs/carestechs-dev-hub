using DevHub.Modules.Identity.Services;
using FluentAssertions;

namespace DevHub.Modules.Identity.Tests;

public class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher _sut = new();

    [Fact]
    public void Hash_ProducesEncodedFormWithPrefixAndParameters()
    {
        var encoded = _sut.Hash("hunter2");
        encoded.Should().StartWith("argon2id$v=19$");
        encoded.Split('$').Should().HaveCount(5);
    }

    [Fact]
    public void Verify_RoundTripsWithSamePassword()
    {
        var hash = _sut.Hash("hunter2");
        _sut.Verify("hunter2", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_FailsWithDifferentPassword()
    {
        var hash = _sut.Hash("hunter2");
        _sut.Verify("hunter3", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_FailsOnMalformedEncoding()
    {
        _sut.Verify("anything", "not-an-argon2-hash").Should().BeFalse();
        _sut.Verify("anything", "argon2id$v=19$m=65536,t=4,p=2$bad-base64$bad-base64").Should().BeFalse();
    }

    [Fact]
    public void TwoHashesOfSamePassword_ProduceDifferentEncodings()
    {
        var a = _sut.Hash("hunter2");
        var b = _sut.Hash("hunter2");
        a.Should().NotBe(b, "the salt must be random per call");
        _sut.Verify("hunter2", a).Should().BeTrue();
        _sut.Verify("hunter2", b).Should().BeTrue();
    }

    [Fact]
    public void Hash_RejectsNullOrEmptyInput()
    {
        Action act1 = () => _sut.Hash(string.Empty);
        act1.Should().Throw<ArgumentException>();
        Action act2 = () => _sut.Hash(null!);
        act2.Should().Throw<ArgumentException>();
    }
}
