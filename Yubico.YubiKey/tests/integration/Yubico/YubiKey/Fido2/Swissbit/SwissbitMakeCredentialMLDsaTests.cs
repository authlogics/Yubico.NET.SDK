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
// Swissbit iShield Key PQC — REAL HARDWARE, post-quantum (ML-DSA / FIPS 204) lifecycle.
//
// This is a TWO-PHASE suite because the beta Swissbit firmware must be physically removed
// and reinserted between credential creation and assertion. State is handed off on disk
// (see SwissbitHandoffStore), exactly as a relying party would persist a registration.
//
//   PHASE 1 (this file) — device INSERTED:
//     dotnet test Yubico.YubiKey\tests\integration\Yubico.YubiKey.IntegrationTests.csproj `
//         -c Debug --filter "FullyQualifiedName~Swissbit&FullyQualifiedName~Phase1"
//
//   >>> Then REMOVE the Swissbit and RE-INSERT it. <<<
//
//   PHASE 2 (SwissbitGetAssertionMLDsaTests) — device REINSERTED:
//     dotnet test ... --filter "FullyQualifiedName~Swissbit&FullyQualifiedName~Phase2"
//
// Run from an ELEVATED shell (FIDO HID access requires Administrator on Windows). Touch the
// device when it blinks. If the device has a PIN, set SWISSBIT_FIDO2_PIN to it first.
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Yubico.YubiKey.Cryptography;
using Yubico.YubiKey.Fido2.Commands;
using Yubico.YubiKey.Fido2.Cose;

namespace Yubico.YubiKey.Fido2.Swissbit
{
    [Trait("Category", "Elevated")]
    public class SwissbitMakeCredentialMLDsaTests : SwissbitFido2TestBase
    {
        private static readonly CoseAlgorithmIdentifier[] AllMlDsa =
        {
            CoseAlgorithmIdentifier.MLDSA44,
            CoseAlgorithmIdentifier.MLDSA65,
            CoseAlgorithmIdentifier.MLDSA87,
        };

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

            using Fido2Session session = OpenSession(
                PinUvAuthTokenPermissions.MakeCredential
                | PinUvAuthTokenPermissions.GetAssertion
                | PinUvAuthTokenPermissions.CredentialManagement);

            // Remove this suite's own leftover credentials from previous runs (scoped to our RP only,
            // so we never delete the operator's real credentials for other relying parties).
            TryCleanupOwnRp(session);

            List<CoseAlgorithmIdentifier> variants = SelectMlDsaVariants(session);
            Skip.If(variants.Count == 0, "Device advertises no ML-DSA variants; nothing to create.");

            var records = new List<SwissbitCredentialRecord>();
            foreach (CoseAlgorithmIdentifier variant in variants)
            {
                TryCreate(session, variant, discoverable: false, records);
                TryCreate(session, variant, discoverable: true, records);
            }

            Skip.If(records.Count == 0, "No ML-DSA credentials could be created on this device. See swissbit-diag.log.");

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
                // The device may not support this variant, or non-resident ML-DSA, or it may be out
                // of resident slots. Log and continue — a real assertion failure (XunitException)
                // still propagates and fails the test.
                string status = ex is Fido2Exception fe && fe.Status is { } s ? $" [CtapStatus={s}]" : "";
                Diag($"SKIP {label}: {ex.GetType().FullName}: {ex.Message}{status}");
            }
        }

        private static List<CoseAlgorithmIdentifier> SelectMlDsaVariants(Fido2Session session)
        {
            IReadOnlyList<Tuple<string, CoseAlgorithmIdentifier>>? advertised =
                session.AuthenticatorInfo.Algorithms;

            if (advertised is not null)
            {
                List<CoseAlgorithmIdentifier> fromInfo = advertised
                    .Select(entry => entry.Item2)
                    .Where(alg => AllMlDsa.Contains(alg))
                    .Distinct()
                    .ToList();

                if (fromInfo.Count > 0)
                {
                    Diag($"ML-DSA variants advertised: {string.Join(", ", fromInfo)}");
                    return fromInfo;
                }
            }

            // The device did not advertise an algorithms list (or none were ML-DSA). Try all three;
            // unsupported ones are skipped at MakeCredential time.
            Diag("No ML-DSA variants advertised; trying all three (best effort).");
            return AllMlDsa.ToList();
        }

        private static void TryCleanupOwnRp(Fido2Session session)
        {
            try
            {
                foreach (RelyingParty rp in session.EnumerateRelyingParties())
                {
                    if (!string.Equals(rp.Id, Rp.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var cred in session.EnumerateCredentialsForRelyingParty(rp))
                    {
                        session.DeleteCredential(cred.CredentialId);
                        Diag("cleanup: deleted a prior credential for this suite's RP.");
                    }
                }
            }
            catch (Exception ex)
            {
                Diag($"cleanup skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
