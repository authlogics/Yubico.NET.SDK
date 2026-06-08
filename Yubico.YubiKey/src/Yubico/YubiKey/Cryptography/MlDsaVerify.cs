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
using System.Security.Cryptography;
using Yubico.YubiKey.Fido2.Cose;

// ML-DSA is an experimental API in .NET 10 (diagnostic SYSLIB5006). This file is the only place
// in the SDK that references the System.Security.Cryptography.MLDsa types, so the suppression is
// scoped here rather than project-wide.
#pragma warning disable SYSLIB5006

namespace Yubico.YubiKey.Cryptography
{
    /// <summary>
    /// This class can verify an ML-DSA (FIPS 204) signature using a COSE public key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class uses the .NET <see cref="MLDsa"/> implementation to verify a signature. ML-DSA
    /// verification is performed in "pure" mode over the raw message with an empty context: there is
    /// <b>no</b> pre-hash, unlike ECDSA. Pass the exact signed message (for FIDO2 this is
    /// <c>authenticatorData || clientDataHash</c>) to <see cref="VerifyData"/>.
    /// </para>
    /// <para>
    /// ML-DSA support depends on the platform. Check <see cref="IsSupported"/> before use; if it is
    /// <c>false</c>, the constructor will throw.
    /// </para>
    /// <para>
    /// The <see cref="MLDsa"/> object is disposable, which is why this class is as well.
    /// </para>
    /// </remarks>
    public sealed class MlDsaVerify : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// Whether ML-DSA is supported on the current platform.
        /// </summary>
        public static bool IsSupported => MLDsa.IsSupported;

        /// <summary>
        /// The object that performs the verification operation, holding the public key.
        /// </summary>
        public MLDsa MlDsa { get; }

        // The default constructor explicitly defined. We don't want it to be used.
        private MlDsaVerify()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Create an instance of the <see cref="MlDsaVerify"/> class using the COSE ML-DSA public key.
        /// </summary>
        /// <param name="coseKey">
        /// The public key to use to verify. Must be a <see cref="CoseMlDsaPublicKey"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="coseKey"/> argument is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The key is not an ML-DSA public key.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// The algorithm is not a supported ML-DSA algorithm.
        /// </exception>
        /// <exception cref="PlatformNotSupportedException">
        /// ML-DSA is not supported on the current platform.
        /// </exception>
        public MlDsaVerify(CoseKey coseKey)
        {
            if (coseKey is null)
            {
                throw new ArgumentNullException(nameof(coseKey));
            }

            if (coseKey is not CoseMlDsaPublicKey mlDsaKey)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.CurrentCulture, ExceptionMessages.UnsupportedAlgorithm),
                    nameof(coseKey));
            }

            MLDsaAlgorithm algorithm = coseKey.Algorithm switch
            {
                CoseAlgorithmIdentifier.MLDSA44 => MLDsaAlgorithm.MLDsa44,
                CoseAlgorithmIdentifier.MLDSA65 => MLDsaAlgorithm.MLDsa65,
                CoseAlgorithmIdentifier.MLDSA87 => MLDsaAlgorithm.MLDsa87,
                _ => throw new NotSupportedException(
                    string.Format(CultureInfo.CurrentCulture, ExceptionMessages.UnsupportedAlgorithm))
            };

            MlDsa = MLDsa.ImportMLDsaPublicKey(algorithm, mlDsaKey.PublicKey.ToArray());
        }

        /// <summary>
        /// Verify the <paramref name="signature"/> over <paramref name="message"/>.
        /// </summary>
        /// <remarks>
        /// The <paramref name="message"/> is verified directly (ML-DSA "pure" mode, empty context).
        /// Do not pre-hash the data.
        /// </remarks>
        /// <param name="message">
        /// The exact data that was signed.
        /// </param>
        /// <param name="signature">
        /// The signature to verify.
        /// </param>
        /// <returns>
        /// A boolean, <c>true</c> if the signature verifies, <c>false</c> if it does not.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The <paramref name="message"/> or <paramref name="signature"/> argument is null.
        /// </exception>
        public bool VerifyData(byte[] message, byte[] signature)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (signature is null)
            {
                throw new ArgumentNullException(nameof(signature));
            }

            // Use the explicit 3-argument overload (empty/null context). The 2-argument call is
            // ambiguous between the byte[] and ReadOnlySpan<byte> context overloads.
            return MlDsa.VerifyData(message, signature, (byte[]?)null);
        }

        /// <summary>
        /// Releases the resources used by the <see cref="MLDsa"/> instance.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                MlDsa.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}

#pragma warning restore SYSLIB5006
