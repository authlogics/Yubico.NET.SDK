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

namespace Yubico.YubiKey.Fido2.Cose
{
    /// <summary>
    /// Internal helpers for working with COSE algorithm identifiers.
    /// </summary>
    internal static class CoseKeyHelpers
    {
        /// <summary>
        /// Returns <c>true</c> if the algorithm is one of the ML-DSA (FIPS 204) parameter sets.
        /// </summary>
        public static bool IsMlDsaAlgorithm(CoseAlgorithmIdentifier algorithm) =>
            algorithm is CoseAlgorithmIdentifier.MLDSA44
                or CoseAlgorithmIdentifier.MLDSA65
                or CoseAlgorithmIdentifier.MLDSA87;
    }
}
