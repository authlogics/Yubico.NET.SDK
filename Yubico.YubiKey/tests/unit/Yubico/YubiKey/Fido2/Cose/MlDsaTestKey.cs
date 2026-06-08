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
using System.Security.Cryptography;

// This is the only test file that uses the experimental BCL ML-DSA API directly (to generate keys
// and produce real signatures for the verification tests). The suppression is scoped here.
#pragma warning disable SYSLIB5006

namespace Yubico.YubiKey.Fido2.Cose
{
    /// <summary>
    /// Test helper that wraps a freshly generated ML-DSA key, used to produce real public keys and
    /// signatures for the verification unit tests. Gate use on <see cref="IsSupported"/>.
    /// </summary>
    internal sealed class MlDsaTestKey : IDisposable
    {
        private readonly MLDsa _key;

        public CoseAlgorithmIdentifier Algorithm { get; }

        public byte[] PublicKeyBytes { get; }

        private MlDsaTestKey(MLDsa key, CoseAlgorithmIdentifier algorithm, byte[] publicKeyBytes)
        {
            _key = key;
            Algorithm = algorithm;
            PublicKeyBytes = publicKeyBytes;
        }

        public static bool IsSupported => MLDsa.IsSupported;

        public static MlDsaTestKey Generate(CoseAlgorithmIdentifier algorithm)
        {
            MLDsaAlgorithm parameterSet = algorithm switch
            {
                CoseAlgorithmIdentifier.MLDSA44 => MLDsaAlgorithm.MLDsa44,
                CoseAlgorithmIdentifier.MLDSA65 => MLDsaAlgorithm.MLDsa65,
                CoseAlgorithmIdentifier.MLDSA87 => MLDsaAlgorithm.MLDsa87,
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
            };

            MLDsa key = MLDsa.GenerateKey(parameterSet);
            byte[] publicKey = new byte[PublicKeySize(algorithm)];
            key.ExportMLDsaPublicKey(publicKey);
            return new MlDsaTestKey(key, algorithm, publicKey);
        }

        public CoseMlDsaPublicKey CreateCosePublicKey() =>
            CoseMlDsaPublicKey.CreateFromPublicKeyData(PublicKeyBytes, Algorithm);

        public byte[] Sign(byte[] message)
        {
            byte[] signature = new byte[SignatureSize(Algorithm)];
            _key.SignData(message, signature, context: []);
            return signature;
        }

        public void Dispose() => _key.Dispose();

        public static int PublicKeySize(CoseAlgorithmIdentifier algorithm) => algorithm switch
        {
            CoseAlgorithmIdentifier.MLDSA44 => 1312,
            CoseAlgorithmIdentifier.MLDSA65 => 1952,
            CoseAlgorithmIdentifier.MLDSA87 => 2592,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };

        public static int SignatureSize(CoseAlgorithmIdentifier algorithm) => algorithm switch
        {
            CoseAlgorithmIdentifier.MLDSA44 => 2420,
            CoseAlgorithmIdentifier.MLDSA65 => 3309,
            CoseAlgorithmIdentifier.MLDSA87 => 4627,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
    }
}

#pragma warning restore SYSLIB5006
