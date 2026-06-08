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
using System.IO;
using System.Text;
using Xunit;

namespace Yubico.YubiKey.Fido2.Swissbit
{
    /// <summary>
    /// Shared setup for the Swissbit real-hardware ML-DSA tests. Intentionally minimal and
    /// <b>non-destructive</b>: unlike the YubiKey <c>FidoSessionIntegrationTestBase</c>, the
    /// constructor does NOT enumerate-and-delete credentials, because the credential created in
    /// phase 1 must survive until phase 2 reads it back (after the device is removed and reinserted).
    /// Cleanup is opt-in and scoped to this suite's own relying party — see
    /// <see cref="SwissbitMakeCredentialMLDsaTests"/>.
    /// </summary>
    public abstract class SwissbitFido2TestBase : IDisposable
    {
        /// <summary>
        /// A dedicated relying party so the suite never touches real credentials registered for
        /// other relying parties on the device.
        /// </summary>
        protected static readonly RelyingParty Rp = new("swissbit-pqc.intercede.test")
        {
            Name = "Swissbit PQC Integration Test"
        };

        /// <summary>A fixed 32-byte client-data hash (content is irrelevant to correctness).</summary>
        protected static readonly byte[] ClientDataHash =
            "12345678123456781234567812345678"u8.ToArray();

        /// <summary>
        /// The device PIN. Override with <c>SWISSBIT_FIDO2_PIN</c>; defaults to the SDK test PIN.
        /// A PIN-less device has this set in phase 1.
        /// </summary>
        protected static ReadOnlyMemory<byte> DevicePin
        {
            get
            {
                string? env = Environment.GetEnvironmentVariable("SWISSBIT_FIDO2_PIN");
                return string.IsNullOrEmpty(env)
                    ? "11234567"u8.ToArray()
                    : Encoding.UTF8.GetBytes(env);
            }
        }

        private readonly SwissbitKeyCollector _keyCollector = new(DevicePin);

        private bool _disposed;

        static SwissbitFido2TestBase()
        {
            // Truncate the diagnostics file once per test run. xUnit captures (and discards) Console
            // output for skipped/passed tests, so diagnostics are written to a file instead.
            try
            {
                File.WriteAllText(DiagPath, "=== Swissbit diag run ===" + Environment.NewLine);
            }
            catch
            {
                // Diagnostics are best-effort.
            }
        }

        /// <summary>
        /// Diagnostics file path. Override with <c>SWISSBIT_DIAG</c>; defaults next to the handoff
        /// file. Used because xUnit discards captured Console output for non-failing tests.
        /// </summary>
        protected static string DiagPath =>
            Environment.GetEnvironmentVariable("SWISSBIT_DIAG")
            ?? Path.Combine(
                Path.GetDirectoryName(SwissbitHandoffStore.FilePath) ?? Path.GetTempPath(),
                "swissbit-diag.log");

        /// <summary>Logs a diagnostic line to both the console and the diagnostics file.</summary>
        protected static void Diag(string message)
        {
            Console.WriteLine("[swissbit] " + message);
            try
            {
                File.AppendAllText(DiagPath, message + Environment.NewLine);
            }
            catch
            {
                // Diagnostics are best-effort.
            }
        }

        protected IYubiKeyDevice Device => field ??= SwissbitFido2Device.GetDevice();

        /// <summary>
        /// Opens a FIDO2 session on the device and sets the PIN if none is enrolled. Deliberately does
        /// NOT pre-verify the PIN or call credential management: MakeCredential/GetAssertions drive PIN
        /// collection via the <see cref="SwissbitKeyCollector"/>, mirroring the windows-desktop-logon-agent's
        /// proven flow. This keeps the CTAP-HID command sequence before the touch-required command
        /// minimal — working theory: this beta key tolerates only a short command sequence per power
        /// cycle, and extra traffic (cleanup enumerate, permission-scoped token) provokes
        /// CTAPHID_ERR_CHANNEL_BUSY.
        /// </summary>
        protected Fido2Session OpenSession()
        {
            Fido2Session? session = null;
            try
            {
                session = new Fido2Session(Device)
                {
                    KeyCollector = _keyCollector.HandleRequest
                };

                // Discovery uses FindHidDevices(), so the background listener never started; this is a
                // safety net only.
                QuiesceDeviceListener();

                if (session.AuthenticatorInfo.ForcePinChange == true ||
                    session.AuthenticatorInfo.GetOptionValue(AuthenticatorOptions.clientPin) == OptionValue.False)
                {
                    Diag("No PIN enrolled (or change forced); setting the test PIN.");
                    _ = session.TrySetPin(DevicePin);
                }

                return session;
            }
            catch
            {
                session?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Stops the background <see cref="YubiKeyDeviceListener"/> so it stops re-enumerating and
        /// re-opening the device while a touch-required command is in flight. Best-effort.
        /// </summary>
        protected static void QuiesceDeviceListener()
        {
            try
            {
                // Discovery uses FindHidDevices() (see SwissbitFido2Device), so the listener should
                // never have started. This is a safety net in case anything else started it.
                if (YubiKeyDeviceListener.IsListenerRunning)
                {
                    YubiKeyDeviceListener.StopListening();
                    Diag("Stopped background YubiKeyDeviceListener to avoid CTAPHID channel contention.");
                }
            }
            catch (Exception ex)
            {
                Diag($"Could not stop YubiKeyDeviceListener: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Logs the device's versions, options and PIN/UV protocols for diagnostics.</summary>
        protected static void DumpDeviceInfo(Fido2Session session)
        {
            var info = session.AuthenticatorInfo;
            Diag($"versions: {string.Join(", ", info.Versions)}");
            Diag($"AAGUID: {Convert.ToHexString(info.Aaguid.ToArray())}");
            if (info.Options is not null)
            {
                foreach (var option in info.Options)
                {
                    Diag($"option {option.Key} = {option.Value}");
                }
            }

            if (info.PinUvAuthProtocols is not null)
            {
                Diag($"pinUvAuthProtocols: {string.Join(", ", info.PinUvAuthProtocols)}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
