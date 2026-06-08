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
using System.Collections.Generic;
using System.Formats.Cbor;
using Xunit;
using Yubico.YubiKey.Fido2.Cose;

namespace Yubico.YubiKey.Fido2
{
    public class MakeCredentialDataTests
    {
        public static TheoryData<CoseAlgorithmIdentifier> MlDsaVariants => new()
        {
            CoseAlgorithmIdentifier.MLDSA44,
            CoseAlgorithmIdentifier.MLDSA65,
            CoseAlgorithmIdentifier.MLDSA87,
        };

        private static readonly byte[] TestClientDataHash = new byte[] {
            0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38,
            0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38
        };

        // MakeCredential authenticator data with attested credential data (AT flag set):
        // rpIdHash(32) || flags || signCount(4) || aaguid(16) || credIdLen(2) || credId || cosePublicKey
        private static byte[] BuildAttestedAuthData(byte[] cosePublicKey, byte[] credId)
        {
            var authData = new List<byte>();
            for (int i = 0; i < 32; i++)
            {
                authData.Add((byte)i); // rpIdHash
            }

            authData.Add(0x41); // flags: UP (0x01) | AT (0x40)
            authData.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x05 }); // signCount
            authData.AddRange(new byte[16]); // aaguid
            authData.Add((byte)(credId.Length >> 8));
            authData.Add((byte)(credId.Length & 0xFF));
            authData.AddRange(credId);
            authData.AddRange(cosePublicKey);
            return authData.ToArray();
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            byte[] result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        // { 1: "packed", 2: authData, 3: { "alg": alg, "sig": signature } }  (self-attestation, no x5c)
        private static byte[] BuildPackedSelfAttestation(
            byte[] authData, CoseAlgorithmIdentifier algorithm, byte[] signature)
        {
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(3);

            writer.WriteInt32(1);
            writer.WriteTextString("packed");

            writer.WriteInt32(2);
            writer.WriteByteString(authData);

            writer.WriteInt32(3);
            writer.WriteStartMap(2);
            writer.WriteTextString("alg");
            writer.WriteInt32((int)algorithm);
            writer.WriteTextString("sig");
            writer.WriteByteString(signature);
            writer.WriteEndMap();

            writer.WriteEndMap();
            return writer.Encode();
        }

        [Theory]
        [MemberData(nameof(MlDsaVariants))]
        public void Constructor_MlDsaSelfAttestation_ParsesAkpCredential(CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaTestKey.IsSupported)
            {
                return;
            }

            using var key = MlDsaTestKey.Generate(algorithm);
            byte[] cosePublicKey = key.CreateCosePublicKey().Encode();
            byte[] authData = BuildAttestedAuthData(cosePublicKey, new byte[16]);
            byte[] signature = key.Sign(Concat(authData, TestClientDataHash));
            byte[] encoded = BuildPackedSelfAttestation(authData, algorithm, signature);

            var mcData = new MakeCredentialData(encoded);

            var credKey = Assert.IsType<CoseMlDsaPublicKey>(mcData.AuthenticatorData.CredentialPublicKey);
            Assert.Equal(algorithm, credKey.Algorithm);
            Assert.Equal(algorithm, mcData.AttestationAlgorithm);
            Assert.Null(mcData.AttestationCertificates); // self-attestation: no x5c
        }

        [Theory]
        [MemberData(nameof(MlDsaVariants))]
        public void VerifyAttestation_MlDsaSelfAttestation_Succeeds(CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaTestKey.IsSupported)
            {
                return;
            }

            using var key = MlDsaTestKey.Generate(algorithm);
            byte[] cosePublicKey = key.CreateCosePublicKey().Encode();
            byte[] authData = BuildAttestedAuthData(cosePublicKey, new byte[16]);
            byte[] signature = key.Sign(Concat(authData, TestClientDataHash));
            byte[] encoded = BuildPackedSelfAttestation(authData, algorithm, signature);

            var mcData = new MakeCredentialData(encoded);

            Assert.True(mcData.VerifyAttestation(TestClientDataHash));
        }

        [Theory]
        [MemberData(nameof(MlDsaVariants))]
        public void VerifyAttestation_MlDsaSelfAttestation_TamperedClientDataHash_ReturnsFalse(
            CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaTestKey.IsSupported)
            {
                return;
            }

            using var key = MlDsaTestKey.Generate(algorithm);
            byte[] cosePublicKey = key.CreateCosePublicKey().Encode();
            byte[] authData = BuildAttestedAuthData(cosePublicKey, new byte[16]);
            byte[] signature = key.Sign(Concat(authData, TestClientDataHash));
            byte[] encoded = BuildPackedSelfAttestation(authData, algorithm, signature);

            byte[] tamperedHash = (byte[])TestClientDataHash.Clone();
            tamperedHash[0] ^= 0xFF;

            var mcData = new MakeCredentialData(encoded);

            Assert.False(mcData.VerifyAttestation(tamperedHash));
        }

        [Fact]
        public void Constructor_NonEcNonAkpCredentialKey_ThrowsCtap2DataException()
        {
            // An EdDSA (OKP) credential public key is neither EC2 nor AKP, so the credential-type
            // gate must still reject it (regression: the relaxation only added AKP, not OKP).
            byte[] edDsaKey = CoseEdDsaPublicKey.CreateFromPublicKeyData(new byte[32]).Encode();
            byte[] authData = BuildAttestedAuthData(edDsaKey, new byte[16]);
            byte[] encoded = BuildPackedSelfAttestation(authData, CoseAlgorithmIdentifier.EdDSA, new byte[64]);

            _ = Assert.Throws<Ctap2DataException>(() => new MakeCredentialData(encoded));
        }
    }
}
