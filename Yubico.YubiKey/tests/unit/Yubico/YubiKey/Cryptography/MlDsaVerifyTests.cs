// Copyright 2025 Yubico AB
//
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Xunit;
using Yubico.YubiKey.Fido2.Cose;

namespace Yubico.YubiKey.Cryptography
{
    public class MlDsaVerifyTests
    {
        public static TheoryData<CoseAlgorithmIdentifier> Variants => new()
        {
            CoseAlgorithmIdentifier.MLDSA44,
            CoseAlgorithmIdentifier.MLDSA65,
            CoseAlgorithmIdentifier.MLDSA87,
        };

        private static readonly byte[] Message = "the quick brown fox jumps over the lazy dog"u8.ToArray();

        [Fact]
        public void Constructor_NullKey_ThrowsArgumentNull()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new MlDsaVerify(null!));
        }

        [Fact]
        public void Constructor_NonMlDsaKey_ThrowsArgumentException()
        {
            // An EdDSA key is a valid CoseKey but not ML-DSA.
            var edKey = CoseEdDsaPublicKey.CreateFromPublicKeyData(new byte[32]);

            _ = Assert.Throws<ArgumentException>(() => new MlDsaVerify(edKey));
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void VerifyData_ValidSignature_ReturnsTrue(CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaVerify.IsSupported)
            {
                return;
            }

            using var key = MlDsaTestKey.Generate(algorithm);
            byte[] signature = key.Sign(Message);

            using var verifier = new MlDsaVerify(key.CreateCosePublicKey());

            Assert.True(verifier.VerifyData(Message, signature));
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void VerifyData_TamperedMessage_ReturnsFalse(CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaVerify.IsSupported)
            {
                return;
            }

            using var key = MlDsaTestKey.Generate(algorithm);
            byte[] signature = key.Sign(Message);

            byte[] tampered = (byte[])Message.Clone();
            tampered[0] ^= 0xFF;

            using var verifier = new MlDsaVerify(key.CreateCosePublicKey());

            Assert.False(verifier.VerifyData(tampered, signature));
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void VerifyData_TamperedSignature_ReturnsFalse(CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaVerify.IsSupported)
            {
                return;
            }

            using var key = MlDsaTestKey.Generate(algorithm);
            byte[] signature = key.Sign(Message);
            signature[0] ^= 0xFF;

            using var verifier = new MlDsaVerify(key.CreateCosePublicKey());

            Assert.False(verifier.VerifyData(Message, signature));
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void VerifyData_WrongKey_ReturnsFalse(CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaVerify.IsSupported)
            {
                return;
            }

            using var signingKey = MlDsaTestKey.Generate(algorithm);
            using var otherKey = MlDsaTestKey.Generate(algorithm);
            byte[] signature = signingKey.Sign(Message);

            using var verifier = new MlDsaVerify(otherKey.CreateCosePublicKey());

            Assert.False(verifier.VerifyData(Message, signature));
        }
    }
}
