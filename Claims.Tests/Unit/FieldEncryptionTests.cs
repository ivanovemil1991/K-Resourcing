using System.Security.Cryptography;
using Claims.Infrastructure.Security;
using Claims.Tests.Fakes;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Claims.Tests.Unit;

public class FieldEncryptionTests
{
    private readonly AesGcmFieldEncryptor _encryptor = TestData.Encryptor;

    [Fact]
    public void Text_round_trips_including_unicode()
    {
        const string name = "Zoë Müller 山田";

        var stored = _encryptor.Encrypt(name);

        Assert.StartsWith("v1:", stored);
        Assert.DoesNotContain("Müller", stored);
        Assert.Equal(name, _encryptor.Decrypt(stored));
    }

    [Fact]
    public void The_same_text_is_encrypted_differently_each_time()
    {
        Assert.NotEqual(_encryptor.Encrypt("John"), _encryptor.Encrypt("John"));
    }

    [Fact]
    public void Tampered_values_are_rejected()
    {
        var bytes = Convert.FromBase64String(_encryptor.Encrypt("John")[3..]);
        bytes[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => _encryptor.Decrypt("v1:" + Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void A_different_key_cannot_decrypt()
    {
        var stored = _encryptor.Encrypt("John");

        Assert.ThrowsAny<CryptographicException>(() => AesGcmFieldEncryptor.CreateEphemeral().Decrypt(stored));
    }

    [Fact]
    public void Plain_text_without_the_version_prefix_is_rejected()
    {
        Assert.Throws<CryptographicException>(() => _encryptor.Decrypt("John"));
    }

    [Theory]
    [InlineData("Development", "", true)]
    [InlineData("Production", "", false)]
    [InlineData("Production", "not-base64!", false)]
    [InlineData("Production", "AAAA", false)]
    [InlineData("Production", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", true)]
    public void The_key_is_validated_outside_development(string environment, string key, bool valid)
    {
        var validator = new EncryptionOptionsValidator(new FakeEnvironment(environment));

        Assert.Equal(valid, validator.Validate(null, new EncryptionOptions { Key = key }).Succeeded);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Claims";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}