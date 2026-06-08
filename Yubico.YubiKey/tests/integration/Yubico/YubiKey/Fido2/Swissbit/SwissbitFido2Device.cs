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
using System.Linq;
using System.Threading;
using Xunit;
using Yubico.PlatformInterop;

namespace Yubico.YubiKey.Fido2.Swissbit
{
    /// <summary>
    /// Locates a real Swissbit iShield Key PQC (or any non-YubiKey FIDO HID authenticator) through
    /// the Yubico SDK's HID-FIDO transport, for real-hardware integration tests.
    /// </summary>
    /// <remarks>
    /// This is deliberately separate from <c>IntegrationTestDeviceEnumeration</c>/
    /// <c>FidoSessionIntegrationTestBase</c>, which are YubiKey-specific: they assert
    /// <see cref="YubiKeyCapabilities.Fido2"/> in <c>EnabledUsbCapabilities</c> (a Swissbit reports
    /// only <c>FidoU2f</c> because it does not implement YubiKey's proprietary management command),
    /// and they wipe all credentials in their constructor (which would destroy the credential we
    /// must read back after the device is reinserted). The allow-list itself is not a blocker — it
    /// permits any device whose <c>SerialNumber</c> is null, which a Swissbit is over this transport.
    /// <para>
    /// Tests using this helper must be <c>[SkippableFact]</c>/<c>[SkippableTheory]</c> so they skip
    /// (rather than fail) when the runner is not elevated or no FIDO HID device is attached.
    /// </para>
    /// </remarks>
    internal static class SwissbitFido2Device
    {
        /// <summary>
        /// Finds the FIDO HID authenticator. Skips the calling test when the runner is not elevated
        /// or no FIDO HID device is attached.
        /// </summary>
        public static IYubiKeyDevice GetDevice()
        {
            Skip.IfNot(
                SdkPlatformInfo.IsElevated,
                "FIDO HID access on Windows requires the test runner to be elevated (Run as Administrator).");

            // A freshly inserted device can report ready before its HID-FIDO interface
            // (usage page 0xF1D0) is enumerable, so retry for a few seconds before giving up.
            IYubiKeyDevice? device = null;
            int count = 0;
            for (int attempt = 0; attempt < 12 && device is null; attempt++)
            {
                if (attempt > 0)
                {
                    Thread.Sleep(500);
                }

                var devices = YubiKeyDevice.FindByTransport(Transport.HidFido).ToList();
                count = devices.Count;
                device = devices.FirstOrDefault();
            }

            Skip.If(
                device is null,
                "No FIDO HID device found. Insert the Swissbit iShield Key PQC into a USB port and " +
                "run the tests from an elevated shell.");

            if (count > 1)
            {
                Console.WriteLine(
                    $"[swissbit] WARNING: {count} FIDO HID devices are attached; using the first one. " +
                    "Remove other FIDO authenticators (incl. a running VirtualFido server) so the " +
                    "Swissbit is selected unambiguously.");
            }

            return device!;
        }
    }
}
