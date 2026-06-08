# Swissbit iShield Key 2 FIDO2 PQC — real-hardware ML-DSA tests

These integration tests drive a **physical Swissbit post-quantum (ML-DSA / FIPS 204) security key**
through the Yubico SDK and verify a full register → authenticate ceremony.

## ⚠️ REMOVE AND RE-INSERT THE KEY BEFORE EVERY RUN

This is a **non-certified beta** key that tolerates only a *minimal* CTAP-HID command sequence per
power cycle. If you run two touch-required operations on a single insertion, the key returns
`CTAPHID_ERR_CHANNEL_BUSY` (surfaced as a generic `"command failed to complete"`).

So the workflow is **one operation per fresh insertion**:

1. Insert the key → run **Phase 1** (creates exactly one credential) → it persists a handoff file.
2. **Physically remove and re-insert the key.**
3. Run **Phase 2** (one assertion) → it verifies against the persisted public key.
4. To test another variant/type: **remove, re-insert,** then run Phase 1 again.

Touch the key when it blinks. Each Phase 1 run overwrites the handoff with its single credential.

## Prerequisites

- An **elevated** shell (FIDO HID access on Windows requires Administrator).
- `SWISSBIT_FIDO2_PIN` = the key's FIDO2 PIN (set it in Windows "Security Key" settings first if the
  key has no PIN). The harness submits this once; it deliberately does **not** retry, to avoid a lockout.
- .NET 10 (ML-DSA via `System.Security.Cryptography.MLDsa`).

## Environment variables

| Variable | Values | Default | Meaning |
|---|---|---|---|
| `SWISSBIT_FIDO2_PIN` | string | `11234567` | Device FIDO2 PIN |
| `SWISSBIT_MLDSA_VARIANT` | `44` \| `65` \| `87` | `44` | ML-DSA parameter set for Phase 1 |
| `SWISSBIT_RK` | `true` \| `false` | `false` | Create a discoverable (resident) credential |
| `SWISSBIT_PQC_HANDOFF` | path | OS temp `swissbit-pqc-handoff.json` | Phase 1 → Phase 2 handoff file |
| `SWISSBIT_DIAG` | path | next to handoff | Diagnostics log (xUnit discards console output for non-failing tests) |

## Run it (PowerShell, elevated)

```powershell
# --- Phase 1: key freshly inserted ---
$env:SWISSBIT_FIDO2_PIN = "<device-pin>"
$env:SWISSBIT_MLDSA_VARIANT = "65"      # 44 | 65 | 87
$env:SWISSBIT_RK = "false"              # true => discoverable/resident
dotnet test Yubico.YubiKey\tests\integration\Yubico.YubiKey.IntegrationTests.csproj `
    -c Debug --filter "FullyQualifiedName~Phase1_MakeCredentials"

# >>> REMOVE the key, then RE-INSERT it <<<

# --- Phase 2: key freshly re-inserted ---
dotnet test Yubico.YubiKey\tests\integration\Yubico.YubiKey.IntegrationTests.csproj `
    -c Debug --filter "FullyQualifiedName~Phase2"
```

`Phase1_GetInfo_AdvertisesMlDsa` is a separate, PIN-free diagnostic (dumps the key's options); it
opens its own session, so run it on its own fresh insertion if you use it.

## Design notes (why it's shaped this way)

- **Discovery uses `YubiKeyDevice.FindHidDevices()`**, not `FindByTransport`. `FindByTransport` starts
  the background `YubiKeyDeviceListener` (+ smartcard listener); this key's CCID interface flaps
  `PRESENT`/`UNPOWERED`, so the listener keeps re-enumerating and re-opens the FIDO interface — a
  second CTAPHID channel that causes `CHANNEL_BUSY`. **Do not switch this back.**
- **Agent-style session**: no PIN pre-verification and no credential-management calls;
  `MakeCredential`/`GetAssertions` drive PIN/touch. This keeps the per-power-cycle command sequence
  minimal and mirrors the proven `windows-desktop-logon-agent` flow.
- **Test-harness only.** No SDK production source was changed to support this key. The lone related
  SDK change (`AuthenticatorInfo` reading `vendorPrototypeConfigCommands` as unsigned) is a real
  CTAP 2.1 conformance fix.
- The tests **skip cleanly** when no device / not elevated / no handoff is present.

## Verified coverage (on the physical key, AAGUID `817CDAB8…`)

| Path | ML-DSA-44 | ML-DSA-65 | ML-DSA-87 |
|---|---|---|---|
| MakeCredential + packed self-attestation verified | ✅ | ✅ | ✅ |
| Non-resident assertion (allow-list) | ✅ | — | — |
| Resident/discoverable assertion (no allow-list) | — | ✅ | — |
