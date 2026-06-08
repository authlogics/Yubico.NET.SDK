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
using System.IO;
using System.Text.Json;

namespace Yubico.YubiKey.Fido2.Swissbit
{
    /// <summary>
    /// One credential created in phase 1 and verified in phase 2. Everything a relying party would
    /// persist after a registration ceremony: the credential id, the COSE public key (as raw ML-DSA
    /// bytes plus the algorithm id), and the request context.
    /// </summary>
    internal sealed class SwissbitCredentialRecord
    {
        /// <summary>Human-readable label, e.g. <c>MLDSA65-resident</c>.</summary>
        public string Label { get; set; } = "";

        /// <summary><c>(int)CoseAlgorithmIdentifier</c> (-48 / -49 / -50).</summary>
        public int Algorithm { get; set; }

        /// <summary>Base64 of the raw FIPS 204 public key bytes.</summary>
        public string PublicKeyB64 { get; set; } = "";

        /// <summary>Base64 of the credential id bytes.</summary>
        public string CredentialIdB64 { get; set; } = "";

        /// <summary>Credential id type, normally <c>public-key</c>.</summary>
        public string CredentialType { get; set; } = "public-key";

        /// <summary>True if a discoverable (resident) credential was requested.</summary>
        public bool Discoverable { get; set; }

        public string RpId { get; set; } = "";

        public string RpName { get; set; } = "";

        /// <summary>Base64 of the client-data hash used for the credential.</summary>
        public string ClientDataHashB64 { get; set; } = "";
    }

    /// <summary>The on-disk handoff written by phase 1 and read by phase 2.</summary>
    internal sealed class SwissbitHandoff
    {
        public string CreatedUtc { get; set; } = "";

        public List<SwissbitCredentialRecord> Credentials { get; set; } = new();
    }

    /// <summary>
    /// Reads/writes the phase-1 → phase-2 handoff file. The device is physically removed between the
    /// two phases (a beta-firmware requirement at this time), so credential state cannot live in
    /// process memory — it is persisted here instead.
    /// </summary>
    internal static class SwissbitHandoffStore
    {
        /// <summary>
        /// Handoff file path. Override with <c>SWISSBIT_PQC_HANDOFF</c>; defaults to a stable file in
        /// the OS temp directory so both phases (separate <c>dotnet test</c> runs) agree on it.
        /// </summary>
        public static string FilePath =>
            Environment.GetEnvironmentVariable("SWISSBIT_PQC_HANDOFF")
            ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "swissbit-pqc-handoff.json");

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static void Save(SwissbitHandoff handoff) =>
            File.WriteAllText(FilePath, JsonSerializer.Serialize(handoff, Options));

        public static SwissbitHandoff? Load()
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            string json = File.ReadAllText(FilePath);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<SwissbitHandoff>(json);
        }
    }
}
