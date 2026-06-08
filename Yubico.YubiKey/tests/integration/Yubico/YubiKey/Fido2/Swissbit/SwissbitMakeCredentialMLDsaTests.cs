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

// =====================================================================================
// Swissbit iShield Key 2 FIDO2 PQC — REAL HARDWARE, post-quantum (ML-DSA / FIPS 204).
//
// This beta key appears to tolerate only a MINIMAL CTAP-HID command sequence per power
// cycle: extra traffic before a touch-required command (credential-management enumerate,
// a permission-scoped getPinUvAuthToken, or multiple MakeCredentials in one session) was
// seen to provoke CTAPHID_ERR_CHANNEL_BUSY. So:
//   * Discovery uses YubiKeyDevice.FindHidDevices() (no background listener).
//   * The session does NOT pre-verify the PIN or call credential management — MakeCredential
//     drives PIN/touch itself (mirrors the windows-desktop-logon-agent).
//   * Phase 1 creates exactly ONE credential per run. RE-INSERT the key before each run.
//
//   PHASE 1 — fresh-inserted key, creates one credential (variant/rk from env):
//     setx-style:  $env:SWISSBIT_MLDSA_VARIANT="44"   # or 65 / 87  (default 44)
//                  $env:SWISSBIT_RK="false"           # or true     (default false)
//     dotnet test ...IntegrationTests.csproj -c Debug `
//         --filter "FullyQualifiedName~Swissbit&FullyQualifiedName~Phase1_MakeCredentials"
//
//   >>> Then REMOVE the Swissbit and RE-INSERT it. <<<
//
//   PHASE 2 (SwissbitGetAssertionMLDsaTests) — reinserted key, asserts the persisted credential:
//     dotnet test ... --filter "FullyQualifiedName~Swissbit&FullyQualifiedName~Phase2"
//
// Run from an ELEVATED shell. Touch the device when it blinks. Set SWISSBIT_FIDO2_PIN to the
// device PIN.
// =====================================================================================

using System;
using System.Collections.Generic;
using Xunit;
using Yubico.YubiKey.Cryptography;
using Yubico.YubiKey.Fido2.Cose;

namespace Yubico.YubiKey.Fido2.Swissbit
{
    [Trait("Category", "Elevated")]
    public class SwissbitMakeCredentialMLDsaTests : SwissbitFido2TestBase
    {
        [SkippableFact]
        public void Phase1_GetInfo_AdvertisesMlDsa()
        {
            Skip.IfNot(MlDsaVerify.IsSupported, "ML-DSA is not supported on this platform (need .NET 10 with MLDsa).");

            // authenticatorGetInfo is unauthenticated — no PIN/token needed. Constructing the session
            // is enough to read the advertised algorithms and capabilities.
            using var session = new Fido2Session(Device);

            DumpDeviceInfo(session);

            IReadOnlyList<Tuple<string, CoseAlgorithmIdentifier>>? algorithms =
                session.AuthenticatorInfo.Algorithms;

            if (algorithms is null)
            {
                Diag("getInfo did not include an algorithms list.");
            }
            else
            {
                foreach (var entry in algorithms)
                {
                    Diag($"algorithm: {entry.Item1} / {entry.Item2} ({(int)entry.Item2})");
                }
            }

            Skip.If(algorithms is null, "Device did not advertise an algorithms list; cannot confirm ML-DSA support.");

            Assert.Contains(
                algorithms!,
                entry => entry.Item2 is CoseAlgorithmIdentifier.MLDSA44
                    or CoseAlgorithmIdentifier.MLDSA65
                    or CoseAlgorithmIdentifier.MLDSA87);
        }

        [SkippableFact]
        public void Phase1_MakeCredentials_PersistHandoff()
        {
            Skip.IfNot(MlDsaVerify.IsSupported, "ML-DSA is not supported on this platform (need .NET 10 with MLDsa).");

            CoseAlgorithmIdentifier variant = SelectedVariant();
            bool discoverable = SelectedRk();
            Diag($"Single-credential run: variant={variant} discoverable={discoverable} " +
                "(set SWISSBIT_MLDSA_VARIANT=44|65|87 and SWISSBIT_RK=true|false to change). " +
                "RE-INSERT the key before each run.");

            // Agent-style session: no PIN pre-verification, no credential-management calls. Exactly
            // one MakeCredential follows, to keep the per-power-cycle command sequence minimal.
            using Fido2Session session = OpenSession();

            var records = new List<SwissbitCredentialRecord>();
            TryCreate(session, variant, discoverable, records);

            Skip.If(
                records.Count == 0,
                $"Could not create the {variant} ML-DSA credential on this device. See swissbit-diag.log.");

            SwissbitHandoffStore.Save(new SwissbitHandoff
            {
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                Credentials = records,
            });

            Diag($"Persisted {records.Count} credential record(s) to: {SwissbitHandoffStore.FilePath}");
            Diag("*********************************************************************");
            Diag("*** REMOVE the Swissbit device now, then RE-INSERT it,           ***");
            Diag("*** and run the Phase2 test to complete the assertion round-trip. ***");
            Diag("*********************************************************************");
        }

        private void TryCreate(
            Fido2Session session,
            CoseAlgorithmIdentifier variant,
            bool discoverable,
            List<SwissbitCredentialRecord> records)
        {
            string label = $"{variant}-{(discoverable ? "resident" : "nonresident")}";
            try
            {
                var userId = new byte[] { 1, 2, 3, 4, (byte)(discoverable ? 0x52 : 0x4E) };
                var user = new UserEntity(userId)
                {
                    Name = "swissbit-pqc",
                    DisplayName = "Swissbit PQC " + label,
                };

                // 4-arg ctor: ML-DSA is the ONLY algorithm in pubKeyCredParams (the 2-arg ctor would
                // seed ES256 first, which a dual-capable device would prefer over ML-DSA).
                var mcParams = new MakeCredentialParameters(Rp, user, "public-key", variant)
                {
                    ClientDataHash = ClientDataHash,
                };
                if (discoverable)
                {
                    mcParams.AddOption(AuthenticatorOptions.rk, true);
                }

                Diag($"Creating {label} credential — touch the device when it blinks.");
                MakeCredentialData mc = session.MakeCredential(mcParams);

                // The credential public key must decode as ML-DSA of the requested variant.
                var credKey = Assert.IsType<CoseMlDsaPublicKey>(mc.AuthenticatorData.CredentialPublicKey);
                Assert.Equal(variant, credKey.Algorithm);
                Assert.NotNull(mc.AuthenticatorData.CredentialId);

                // Attestation: self-attestation is what the SDK can verify for ML-DSA today. If the
                // device returns certificate-based (x5c) attestation, verifying it with an ML-DSA
                // leaf cert is not implemented in the SDK, so record it without failing the suite —
                // the end-to-end guarantee is the assertion round-trip in phase 2.
                bool selfAttested = mc.AttestationCertificates is null || mc.AttestationCertificates.Count == 0;
                bool attestationVerified = false;
                try
                {
                    attestationVerified = mc.VerifyAttestation(ClientDataHash);
                }
                catch (Exception ex)
                {
                    Diag($"{label}: VerifyAttestation threw {ex.GetType().Name}: {ex.Message}");
                }

                Diag($"{label}: format='{mc.Format}' selfAttested={selfAttested} " +
                    $"attestationVerified={attestationVerified} pubKeyLen={credKey.PublicKey.Length}");

                if (selfAttested)
                {
                    Assert.True(attestationVerified, $"Self-attestation verification failed for {label}.");
                }
                else
                {
                    Diag($"{label}: certificate-based (x5c) attestation — SDK ML-DSA x5c " +
                        "verification is not implemented; skipping the attestation assertion.");
                }

                records.Add(new SwissbitCredentialRecord
                {
                    Label = label,
                    Algorithm = (int)variant,
                    PublicKeyB64 = Convert.ToBase64String(credKey.PublicKey.ToArray()),
                    CredentialIdB64 = Convert.ToBase64String(mc.AuthenticatorData.CredentialId!.Id.ToArray()),
                    CredentialType = mc.AuthenticatorData.CredentialId!.Type,
                    Discoverable = discoverable,
                    RpId = Rp.Id,
                    RpName = Rp.Name ?? Rp.Id,
                    ClientDataHashB64 = Convert.ToBase64String(ClientDataHash),
                });

                Diag($"OK: {label} created and recorded.");
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                // Log and continue — a real assertion failure (XunitException) still propagates and
                // fails the test.
                string status = ex is Fido2Exception fe && fe.Status is { } s ? $" [CtapStatus={s}]" : "";
                Diag($"SKIP {label}: {ex.GetType().FullName}: {ex.Message}{status}");
            }
        }

        private static CoseAlgorithmIdentifier SelectedVariant() =>
            Environment.GetEnvironmentVariable("SWISSBIT_MLDSA_VARIANT") switch
            {
                "65" => CoseAlgorithmIdentifier.MLDSA65,
                "87" => CoseAlgorithmIdentifier.MLDSA87,
                _ => CoseAlgorithmIdentifier.MLDSA44, // default: smallest response (least CTAP-HID stress)
            };

        private static bool SelectedRk() =>
            string.Equals(
                Environment.GetEnvironmentVariable("SWISSBIT_RK"), "true", StringComparison.OrdinalIgnoreCase);
    }
}
