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
using System.Linq;
using Xunit;
using Yubico.YubiKey.Fido2.Cbor;

namespace Yubico.YubiKey.Fido2.Cose
{
    public class CoseMlDsaPublicKeyTests
    {
        public static TheoryData<CoseAlgorithmIdentifier, int> Variants => new()
        {
            { CoseAlgorithmIdentifier.MLDSA44, 1312 },
            { CoseAlgorithmIdentifier.MLDSA65, 1952 },
            { CoseAlgorithmIdentifier.MLDSA87, 2592 },
        };

        private static byte[] DummyKey(int length)
        {
            byte[] key = new byte[length];
            for (int i = 0; i < length; i++)
            {
                key[i] = (byte)(i & 0xFF);
            }

            return key;
        }

        [Fact]
        public void Constants_HaveExpectedValues()
        {
            Assert.Equal(-48, (int)CoseAlgorithmIdentifier.MLDSA44);
            Assert.Equal(-49, (int)CoseAlgorithmIdentifier.MLDSA65);
            Assert.Equal(-50, (int)CoseAlgorithmIdentifier.MLDSA87);
            Assert.Equal(7, (int)CoseKeyType.Akp);
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void CreateFromPublicKeyData_ValidKey_ReturnsExpectedKey(
            CoseAlgorithmIdentifier algorithm, int length)
        {
            byte[] pub = DummyKey(length);

            var coseKey = CoseMlDsaPublicKey.CreateFromPublicKeyData(pub, algorithm);

            Assert.Equal(CoseKeyType.Akp, coseKey.Type);
            Assert.Equal(algorithm, coseKey.Algorithm);
            Assert.True(coseKey.PublicKey.Span.SequenceEqual(pub));
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void CreateFromPublicKeyData_InvalidLength_ThrowsArgumentException(
            CoseAlgorithmIdentifier algorithm, int length)
        {
            byte[] tooShort = DummyKey(length - 1);

            _ = Assert.Throws<ArgumentException>(
                () => CoseMlDsaPublicKey.CreateFromPublicKeyData(tooShort, algorithm));
        }

        [Fact]
        public void CreateFromPublicKeyData_NonMlDsaAlgorithm_ThrowsNotSupported()
        {
            byte[] pub = DummyKey(1312);

            _ = Assert.Throws<NotSupportedException>(
                () => CoseMlDsaPublicKey.CreateFromPublicKeyData(pub, CoseAlgorithmIdentifier.ES256));
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void EncodingRoundtrip_ReturnsMatchingKey(CoseAlgorithmIdentifier algorithm, int length)
        {
            var original = CoseMlDsaPublicKey.CreateFromPublicKeyData(DummyKey(length), algorithm);

            byte[] encoded = original.Encode();
            var decoded = CoseMlDsaPublicKey.CreateFromEncodedKey(encoded);

            Assert.Equal(original.Type, decoded.Type);
            Assert.Equal(original.Algorithm, decoded.Algorithm);
            Assert.True(original.PublicKey.Span.SequenceEqual(decoded.PublicKey.Span));
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void Encode_ValidKey_ContainsRequiredMapEntries(
            CoseAlgorithmIdentifier algorithm, int length)
        {
            byte[] pub = DummyKey(length);
            var coseKey = CoseMlDsaPublicKey.CreateFromPublicKeyData(pub, algorithm);

            byte[] encoded = coseKey.Encode();
            var map = new CborMap<int>(encoded);

            Assert.Equal((int)CoseKeyType.Akp, map.ReadInt32(1)); // kty
            Assert.Equal((int)algorithm, map.ReadInt32(3)); // alg
            Assert.True(map.ReadByteString(-1).Span.SequenceEqual(pub)); // public key
        }

        [Fact]
        public void Encode_NoData_ThrowsInvalidOperation()
        {
            var coseKey = new CoseMlDsaPublicKey();

            _ = Assert.Throws<InvalidOperationException>(() => coseKey.Encode());
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void CoseKeyCreate_MlDsaEncoding_ReturnsMlDsaPublicKey(
            CoseAlgorithmIdentifier algorithm, int length)
        {
            byte[] pub = DummyKey(length);
            byte[] encoded = CoseMlDsaPublicKey.CreateFromPublicKeyData(pub, algorithm).Encode();

            CoseKey key = CoseKey.Create(encoded, out int bytesRead);

            var mlKey = Assert.IsType<CoseMlDsaPublicKey>(key);
            Assert.Equal(algorithm, mlKey.Algorithm);
            Assert.Equal(CoseKeyType.Akp, mlKey.Type);
            Assert.True(bytesRead > 0);
            Assert.True(mlKey.PublicKey.Span.SequenceEqual(pub));
        }

        [Fact]
        public void CreateFromEncodedKey_WrongLengthForAlgorithm_ThrowsArgumentException()
        {
            // An AKP COSE map declaring ML-DSA-65 but carrying a 100-byte key.
            byte[] encoded = new CborMapWriter<int>()
                .Entry(1, (int)CoseKeyType.Akp)
                .Entry(3, (int)CoseAlgorithmIdentifier.MLDSA65)
                .Entry(-1, (ReadOnlyMemory<byte>)DummyKey(100))
                .Encode();

            _ = Assert.Throws<ArgumentException>(() => CoseMlDsaPublicKey.CreateFromEncodedKey(encoded));
        }
    }
}
