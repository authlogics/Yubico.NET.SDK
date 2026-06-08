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
using System.Formats.Cbor;
using Xunit;
using Yubico.YubiKey.Fido2.Cose;

namespace Yubico.YubiKey.Fido2
{
    public class GetAssertionDataTests
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

        // Minimal assertion authenticator data: rpIdHash(32) || flags(UP) || signCount(4).
        // No attested-credential-data (AT) and no extensions (ED).
        private static byte[] BuildAssertionAuthData()
        {
            byte[] authData = new byte[37];
            for (int i = 0; i < 32; i++)
            {
                authData[i] = (byte)i;
            }

            authData[32] = 0x01; // UP flag
            authData[36] = 0x05; // signCount = 5 (big-endian)
            return authData;
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            byte[] result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        // { 1: { "id": credId, "type": "public-key" }, 2: authData, 3: signature }
        private static byte[] BuildGetAssertionEncoding(byte[] authData, byte[] signature, byte[] credId)
        {
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(3);

            writer.WriteInt32(1);
            writer.WriteStartMap(2);
            writer.WriteTextString("id");
            writer.WriteByteString(credId);
            writer.WriteTextString("type");
            writer.WriteTextString("public-key");
            writer.WriteEndMap();

            writer.WriteInt32(2);
            writer.WriteByteString(authData);

            writer.WriteInt32(3);
            writer.WriteByteString(signature);

            writer.WriteEndMap();
            return writer.Encode();
        }

        [Theory]
        [MemberData(nameof(MlDsaVariants))]
        public void VerifyAssertion_MlDsa_Succeeds(CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaTestKey.IsSupported)
            {
                return;
            }

            using var key = MlDsaTestKey.Generate(algorithm);
            byte[] authData = BuildAssertionAuthData();
            byte[] signature = key.Sign(Concat(authData, TestClientDataHash));
            byte[] credId = new byte[] { 0x11, 0x22, 0x33, 0x44 };
            byte[] encoded = BuildGetAssertionEncoding(authData, signature, credId);

            using var aData = new GetAssertionData(encoded);

            Assert.True(aData.VerifyAssertion(key.CreateCosePublicKey(), TestClientDataHash));
        }

        [Theory]
        [MemberData(nameof(MlDsaVariants))]
        public void VerifyAssertion_MlDsa_TamperedClientDataHash_ReturnsFalse(CoseAlgorithmIdentifier algorithm)
        {
            if (!MlDsaTestKey.IsSupported)
            {
                return;
            }

            using var key = MlDsaTestKey.Generate(algorithm);
            byte[] authData = BuildAssertionAuthData();
            byte[] signature = key.Sign(Concat(authData, TestClientDataHash));
            byte[] encoded = BuildGetAssertionEncoding(authData, signature, new byte[] { 0x11, 0x22, 0x33, 0x44 });

            byte[] tamperedHash = (byte[])TestClientDataHash.Clone();
            tamperedHash[0] ^= 0xFF;

            using var aData = new GetAssertionData(encoded);

            Assert.False(aData.VerifyAssertion(key.CreateCosePublicKey(), tamperedHash));
        }

        [Fact]
        public void Verify_Succeeds()
        {
            byte[] clientDataHash = new byte[] {
                0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38,
                0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38
            };
            byte[] pubKeyX = new byte[] {
                0xe3, 0x2b, 0x6b, 0xc6, 0xb2, 0x42, 0x29, 0x2d, 0xf2, 0x9c, 0xd4, 0x35, 0xd2, 0x2d, 0x6a, 0xad,
                0xb1, 0x92, 0x34, 0x5a, 0xd0, 0x97, 0x77, 0x56, 0xca, 0x74, 0x78, 0x6f, 0xcf, 0xf2, 0x5b, 0xaf
            };
            byte[] pubKeyY = new byte[] {
                0xf3, 0xa6, 0x67, 0x8b, 0x84, 0xad, 0x01, 0xaf, 0xfa, 0x0e, 0x23, 0xe9, 0xf1, 0x63, 0xa2, 0xa5,
                0x6c, 0xc8, 0x9b, 0xf7, 0x82, 0x37, 0xfc, 0x2a, 0x2c, 0xdb, 0xc3, 0x31, 0xce, 0xb1, 0x09, 0x28
            };

            byte[] encodedData = new byte[] {
                0xa4, 0x01, 0xa2, 0x62, 0x69, 0x64, 0x58, 0x30, 0xe3, 0x2b, 0x6b, 0xc6, 0xb2, 0x42, 0x29, 0x2d,
                0xf2, 0x9c, 0xd4, 0x35, 0xd2, 0x42, 0x1c, 0x4b, 0xac, 0x73, 0xc9, 0xc5, 0x68, 0xfd, 0x87, 0x9a,
                0x85, 0x5d, 0xaa, 0x51, 0x04, 0xb9, 0x71, 0x5a, 0x0b, 0xf7, 0x3f, 0x95, 0x07, 0xea, 0x12, 0x41,
                0x66, 0x9e, 0x38, 0xc9, 0x79, 0xe8, 0x20, 0xcc, 0x64, 0x74, 0x79, 0x70, 0x65, 0x6a, 0x70, 0x75,
                0x62, 0x6c, 0x69, 0x63, 0x2d, 0x6b, 0x65, 0x79, 0x02, 0x58, 0x25, 0xb0, 0xa7, 0xf8, 0x5f, 0xe2,
                0xe3, 0x1d, 0x2b, 0x1a, 0x58, 0xd3, 0x6b, 0xb7, 0x29, 0x94, 0x45, 0x93, 0x34, 0x2e, 0xad, 0xca,
                0x6d, 0xb6, 0xc1, 0x68, 0x60, 0xcf, 0x70, 0x1c, 0x86, 0xd8, 0x13, 0x05, 0x00, 0x00, 0x00, 0x04,
                0x03, 0x58, 0x47, 0x30, 0x45, 0x02, 0x20, 0x3f, 0x80, 0x02, 0x1a, 0xbd, 0xe1, 0xef, 0xe7, 0xeb,
                0x45, 0x9e, 0xf9, 0x70, 0x99, 0x3d, 0x60, 0xea, 0x28, 0xbf, 0x2a, 0xc2, 0xf2, 0xec, 0x19, 0x56,
                0xf9, 0x59, 0xb6, 0x3b, 0xb0, 0x9d, 0xa8, 0x02, 0x21, 0x00, 0xe1, 0x89, 0x35, 0xbe, 0x4a, 0x0b,
                0x15, 0x87, 0x37, 0x64, 0xf1, 0x0a, 0xec, 0x0f, 0xfe, 0x2a, 0xc4, 0xfe, 0x9b, 0x92, 0xc8, 0x4f,
                0x1a, 0x0d, 0xa2, 0x29, 0x2c, 0xc2, 0x6e, 0x5d, 0x13, 0x65, 0x04, 0xa1, 0x62, 0x69, 0x64, 0x44,
                0x11, 0x22, 0x33, 0x44
            };

            using var aData = new GetAssertionData(encodedData);
            var pubKey = new CoseEcPublicKey(CoseEcCurve.P256, pubKeyX, pubKeyY);

            bool isVerified = aData.VerifyAssertion(pubKey, clientDataHash);
            Assert.True(isVerified);
        }
    }
}
