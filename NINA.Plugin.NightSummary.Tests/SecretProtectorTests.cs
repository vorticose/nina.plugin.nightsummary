using NINA.Plugin.NightSummary.Data;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    // DPAPI is Windows-only; these execute on the Windows CI runner (the suite's
    // standing constraint). They compile everywhere.
    public class SecretProtectorTests {

        [Fact]
        public void ProtectThenUnprotect_RoundTrips() {
            const string secret = "test-app-pw-1234";
            var protectedValue = SecretProtector.Protect(secret);

            Assert.True(SecretProtector.IsProtected(protectedValue));
            Assert.DoesNotContain(secret, protectedValue); // ciphertext must not embed the plaintext
            Assert.Equal(secret, SecretProtector.Unprotect(protectedValue));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Protect_EmptyOrNull_ReturnsEmpty(string input) {
            Assert.Equal("", SecretProtector.Protect(input));
        }

        [Fact]
        public void IsProtected_TrueForProtected_FalseForPlaintextAndEmpty() {
            Assert.True(SecretProtector.IsProtected(SecretProtector.Protect("x")));
            Assert.False(SecretProtector.IsProtected("plaintext-password"));
            Assert.False(SecretProtector.IsProtected(""));
            Assert.False(SecretProtector.IsProtected(null));
        }

        [Fact]
        public void Unprotect_LegacyPlaintext_ReturnedUnchanged() {
            // No marker → treated as a pre-encryption value and passed through.
            Assert.Equal("legacy-plaintext", SecretProtector.Unprotect("legacy-plaintext"));
        }

        [Fact]
        public void Unprotect_CorruptBlob_ReturnsNull() {
            // Marker present but the payload isn't a valid blob for this user/machine.
            Assert.Null(SecretProtector.Unprotect("dpapi:v1:not-a-real-blob"));
        }
    }
}
