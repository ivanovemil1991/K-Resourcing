using Claims.Infrastructure.Security;

namespace Claims.Tests.Fakes;

/// <summary>Shared test values.</summary>
public static class TestData
{
    /// <summary>A readable, repeatable id: <c>Id(1)</c> is always the same Guid.</summary>
    public static Guid Id(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>EF caches the model with its encryptor, so every test in the process must use the same key.</summary>
    public static readonly AesGcmFieldEncryptor Encryptor = new(new byte[32]);
}