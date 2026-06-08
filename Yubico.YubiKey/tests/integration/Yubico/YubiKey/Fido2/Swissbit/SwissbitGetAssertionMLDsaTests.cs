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
// Swissbit iShield Key PQC — PHASE 2 (assertion). Run AFTER Phase1, with the device
// REMOVED and RE-INSERTED. Reads the credentials persisted by phase 1, requests an
// assertion for each, and verifies the ML-DSA signature against the persisted public key.
//
//   dotnet test Yubico.YubiKey\tests\integration\Yubico.YubiKey.IntegrationTests.csproj `
//       -c Debug --filter "FullyQualifiedName~Swissbit&FullyQualifiedName~Phase2"
//
// Elevated shell; touch the device when it blinks; set SWISSBIT_FIDO2_PIN if the device
// has a non-default PIN. See SwissbitMakeCredentialMLDsaTests for the full workflow.
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Yubico.YubiKey.Cryptography;
using Yubico.YubiKey.Fido2.Cose;

namespace Yubico.YubiKey.Fido2.Swissbit
{
    [Trait("Category", "Elevated")]
    public class SwissbitGetAssertionMLDsaTests : SwissbitFido2TestBase
    {
        [SkippableFact]
        public void Phase2_GetAssertions_VerifyAll()
        {
            Skip.IfNot(MlDsaVerify.IsSupported, "ML-DSA is not supported on this platform (need .NET 10 with MLDsa).");

            SwissbitHandoff? handoff = SwissbitHandoffStore.Load();
            Skip.If(
                handoff is null || handoff.Credentials.Count == 0,
                $"No handoff file at {SwissbitHandoffStore.FilePath}. Run the Phase1 test first (device " +
                "inserted), then remove and reinsert the device before running Phase2.");

            Diag(
                $"Loaded {handoff!.Credentials.Count} credential record(s) from " +
                $"{SwissbitHandoffStore.FilePath} (created {handoff.CreatedUtc}).");

            using Fido2Session session = OpenSession();

            int verified = 0;
            var failures = new List<string>();

            foreach (SwissbitCredentialRecord record in handoff.Credentials)
            {
                try
                {
                    VerifyOne(session, record);
                    verified++;
                    Console.WriteLine($"[swissbit] OK: {record.Label} assertion verified after reinsertion.");
                }
                catch (Exception ex)
                {
                    string message = $"{record.Label}: {ex.GetType().Name}: {ex.Message}";
                    failures.Add(message);
                    Console.WriteLine($"[swissbit] FAIL {message}");
                }
            }

            Console.WriteLine($"[swissbit] Verified {verified}/{handoff.Credentials.Count} ML-DSA assertion(s).");

            Assert.True(
                failures.Count == 0,
                "One or more ML-DSA assertions failed after reinsertion:" +
                Environment.NewLine + string.Join(Environment.NewLine, failures));

            Assert.True(verified > 0, "No assertions were verified.");
        }

        private void VerifyOne(Fido2Session session, SwissbitCredentialRecord record)
        {
            var algorithm = (CoseAlgorithmIdentifier)record.Algorithm;
            byte[] publicKey = Convert.FromBase64String(record.PublicKeyB64);
            byte[] credentialId = Convert.FromBase64String(record.CredentialIdB64);
            byte[] clientDataHash = Convert.FromBase64String(record.ClientDataHashB64);

            // Reconstruct the COSE public key exactly as a relying party would from stored bytes.
            CoseMlDsaPublicKey coseKey = CoseMlDsaPublicKey.CreateFromPublicKeyData(publicKey, algorithm);

            var gaParams = new GetAssertionParameters(Rp, clientDataHash);

            // Non-resident credentials must be named in the allow-list (the device re-derives the
            // private key from the credential id). Resident credentials are discoverable, so we omit
            // the allow-list to exercise the discoverable path and match the result by credential id.
            if (!record.Discoverable)
            {
                gaParams.AllowCredential(new CredentialId
                {
                    Type = record.CredentialType,
                    Id = credentialId,
                });
            }

            Console.WriteLine($"[swissbit] Asserting {record.Label} — touch the device when it blinks.");
            IReadOnlyList<GetAssertionData> assertions = session.GetAssertions(gaParams);

            GetAssertionData? match = assertions
                .FirstOrDefault(a => a.CredentialId.Id.Span.SequenceEqual(credentialId));

            Assert.True(
                match is not null,
                $"No assertion returned matching credential {record.Label} " +
                $"(got {assertions.Count} assertion(s)).");

            Assert.True(
                match!.VerifyAssertion(coseKey, clientDataHash),
                $"Assertion signature verification failed for {record.Label}.");
        }
    }
}
