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

namespace Yubico.YubiKey.Fido2.Swissbit
{
    /// <summary>
    /// A <c>KeyCollector</c> for the Swissbit real-hardware tests. Submits a single configured PIN
    /// (from <c>SWISSBIT_FIDO2_PIN</c>, or the default) and logs touch/UV prompts so the operator
    /// knows when to tap the device.
    /// </summary>
    /// <remarks>
    /// Unlike the shared <c>TestKeyCollector</c>, this never resubmits on a PIN retry: a real device
    /// locks (and eventually factory-resets) after a few wrong PINs, so on a retry it aborts with a
    /// clear message instead of burning attempts.
    /// </remarks>
    internal sealed class SwissbitKeyCollector
    {
        private readonly ReadOnlyMemory<byte> _pin;

        public SwissbitKeyCollector(ReadOnlyMemory<byte> pin) => _pin = pin;

        public bool HandleRequest(KeyEntryData data)
        {
            switch (data.Request)
            {
                case KeyEntryRequest.VerifyFido2Pin:
                    if (data.IsRetry)
                    {
                        Console.WriteLine(
                            $"[swissbit] PIN rejected (retries remaining: {data.RetriesRemaining}). " +
                            "Set SWISSBIT_FIDO2_PIN to the device's PIN. Aborting to avoid a lockout.");
                        return false;
                    }

                    data.SubmitValue(_pin.Span);
                    break;

                case KeyEntryRequest.SetFido2Pin:
                    data.SubmitValue(_pin.Span);
                    break;

                case KeyEntryRequest.TouchRequest:
                    Console.WriteLine("[swissbit] Touch the device now (it should be blinking).");
                    break;

                case KeyEntryRequest.VerifyFido2Uv:
                    Console.WriteLine("[swissbit] Biometric/user-verification requested on the device.");
                    break;

                case KeyEntryRequest.Release:
                    break;

                default:
                    Console.WriteLine($"[swissbit] Unsupported key-entry request: {data.Request}");
                    return false;
            }

            return true;
        }
    }
}
