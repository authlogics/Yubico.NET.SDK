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
using System.Globalization;
using Yubico.YubiKey.Fido2.Cbor;

namespace Yubico.YubiKey.Fido2.Cose
{
    /// <summary>
    /// A representation of an ML-DSA (FIPS 204) public key in COSE form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ML-DSA is a post-quantum signature algorithm. In COSE it uses the key type
    /// <see cref="CoseKeyType.Akp"/> (Algorithm Key Pair); the algorithm identifier
    /// (<see cref="CoseAlgorithmIdentifier.MLDSA44"/>, <see cref="CoseAlgorithmIdentifier.MLDSA65"/>
    /// or <see cref="CoseAlgorithmIdentifier.MLDSA87"/>) fully determines the parameter set, so there
    /// is no curve field. The only COSE labels are 1 (key type), 3 (algorithm) and -1 (public key).
    /// </para>
    /// <para>
    /// The public key is the raw FIPS 204 public key. Its length is fixed per parameter set:
    /// 1312 bytes (ML-DSA-44), 1952 bytes (ML-DSA-65) or 2592 bytes (ML-DSA-87).
    /// </para>
    /// </remarks>
    public class CoseMlDsaPublicKey : CoseKey
    {
        // For AKP keys the public key is at label -1 (unlike OKP/EdDSA, where -1 is the
        // curve and -2 is the public key).
        private const int TagPublicKey = -1;

        private const int MlDsa44PublicKeyLength = 1312;
        private const int MlDsa65PublicKeyLength = 1952;
        private const int MlDsa87PublicKeyLength = 2592;

        private byte[] _publicKey = Array.Empty<byte>();

        /// <summary>
        /// The raw ML-DSA public key data.
        /// </summary>
        /// <remarks>
        /// The length must match the parameter set indicated by <see cref="CoseKey.Algorithm"/>.
        /// Length validation is performed by the factory methods
        /// (<see cref="CreateFromPublicKeyData"/> and <see cref="CreateFromEncodedKey"/>); the setter
        /// simply stores the value, so callers should set <see cref="CoseKey.Algorithm"/> before
        /// using the result.
        /// </remarks>
        public ReadOnlyMemory<byte> PublicKey
        {
            get => _publicKey;
            set => _publicKey = value.ToArray();
        }

        /// <summary>
        /// Construct a <see cref="CoseMlDsaPublicKey"/> from raw public key data and an algorithm.
        /// </summary>
        /// <param name="publicKey">
        /// The raw ML-DSA public key bytes.
        /// </param>
        /// <param name="algorithm">
        /// The ML-DSA algorithm identifier (MLDSA44, MLDSA65 or MLDSA87).
        /// </param>
        /// <exception cref="NotSupportedException">
        /// The <paramref name="algorithm"/> is not an ML-DSA algorithm.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The public key data is not the correct length for the algorithm.
        /// </exception>
        public static CoseMlDsaPublicKey CreateFromPublicKeyData(
            ReadOnlyMemory<byte> publicKey,
            CoseAlgorithmIdentifier algorithm)
        {
            ValidateLength(publicKey.Length, algorithm);

            return new CoseMlDsaPublicKey
            {
                Algorithm = algorithm,
                Type = CoseKeyType.Akp,
                PublicKey = publicKey
            };
        }

        /// <summary>
        /// Creates a new instance of <see cref="CoseMlDsaPublicKey"/> from the given encoded COSE key.
        /// </summary>
        /// <param name="encodedCoseKey">
        /// The encoded COSE key in CBOR format.
        /// </param>
        /// <returns>
        /// A <see cref="CoseMlDsaPublicKey"/> initialized with the provided encoded key data.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// The algorithm in the encoding is not an ML-DSA algorithm.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The public key data is not the correct length for the algorithm.
        /// </exception>
        public static CoseMlDsaPublicKey CreateFromEncodedKey(ReadOnlyMemory<byte> encodedCoseKey)
        {
            var map = new CborMap<int>(encodedCoseKey);
            var algorithm = (CoseAlgorithmIdentifier)map.ReadInt32(TagAlgorithm);
            var publicKey = map.ReadByteString(TagPublicKey);
            ValidateLength(publicKey.Length, algorithm);

            return new CoseMlDsaPublicKey
            {
                Algorithm = algorithm,
                Type = (CoseKeyType)map.ReadInt32(TagKeyType),
                PublicKey = publicKey
            };
        }

        /// <inheritdoc/>
        public override byte[] Encode()
        {
            if (_publicKey.Length == 0)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        ExceptionMessages.NoDataToEncode));
            }

            return new CborMapWriter<int>()
                .Entry(TagKeyType, (int)CoseKeyType.Akp)
                .Entry(TagAlgorithm, (int)Algorithm)
                .Entry(TagPublicKey, PublicKey)
                .Encode();
        }

        private static void ValidateLength(int length, CoseAlgorithmIdentifier algorithm)
        {
            int expected = algorithm switch
            {
                CoseAlgorithmIdentifier.MLDSA44 => MlDsa44PublicKeyLength,
                CoseAlgorithmIdentifier.MLDSA65 => MlDsa65PublicKeyLength,
                CoseAlgorithmIdentifier.MLDSA87 => MlDsa87PublicKeyLength,
                _ => throw new NotSupportedException(
                    string.Format(CultureInfo.CurrentCulture, ExceptionMessages.UnsupportedAlgorithm))
            };

            if (length != expected)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        ExceptionMessages.InvalidPublicKeyData));
            }
        }
    }
}
