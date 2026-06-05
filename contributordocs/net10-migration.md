<!-- Copyright 2025 Yubico AB

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

	http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License. -->

# .NET 10 migration

This fork targets a runtime environment that is **.NET 10 only**. To get there without ever leaving the
repository in a non-building state, the migration is split into two phases.

## Phase 1 - additive multi-target (DONE)

Phase 1 added `net10.0` alongside the existing `.NET Standard` targets so that nothing broke for current
consumers while net10 support was brought online. (Phase 1b later dropped the `.NET Standard` targets - see
below - so the descriptions here are historical.)

- `global.json` SDK pinned to the .NET 10 SDK (`10.0.300`, `rollForward: latestFeature`).
- The two production libraries multi-targeted `netstandard2.0;netstandard2.1;net10.0`:
	- `Yubico.Core/src/Yubico.Core.csproj`
	- `Yubico.YubiKey/src/Yubico.YubiKey.csproj`
- Compatibility packages that were only needed on `.NET Standard` were scoped behind a
  `Condition="$(TargetFramework.StartsWith('netstandard'))"` item group so they were not referenced on net10:
	- `Yubico.Core`: `System.Memory`, `Microsoft.Bcl.HashCode`
	- `Yubico.YubiKey`: `Microsoft.Bcl.AsyncInterfaces`
- The in-tree `CryptographicOperations` polyfill (then at
  `Yubico.Core/src/System.Security.Cryptography/CryptographicOperations.cs`) was guarded with
  `#if NETSTANDARD2_0` (the class compiled only for netstandard2.0) and forwarded to the in-box type via
  `TypeForwardedTo` for every other target (netstandard2.1 and net10.0).
- All test and sample projects were moved from `net8.0` to `net10.0`.
- C# `LangVersion` was intentionally **left at `13.0`** in this phase.

### Diagnostics deliberately demoted in Phase 1

The `net10.0` BCL carries newer obsoletions and nullable annotations than `.NET Standard`. Because the build
sets `TreatWarningsAsErrors=true`, these surface as build-breaking errors on the net10 leg only. They are
pre-existing issues (not regressions from the multi-target) whose real fixes belong to Phase 1b, so for Phase 1
they are demoted from error to warning via `WarningsNotAsErrors` in
[`build/CompilerSettings.props`](../build/CompilerSettings.props). They remain visible as warnings so they are
not lost:

| Code(s) | Meaning | Phase 1b fix |
| --- | --- | --- |
| `NU1510` | Restore flags a `PackageReference` that is now provided in-box and would be pruned. | Remove the redundant package (see below). |
| `SYSLIB0004` | Obsolete Constrained Execution Region (CER) attributes used in P/Invoke interop. | Remove the `ReliabilityContract`/CER attributes. |
| `SYSLIB0051` | Obsolete formatter-based serialization constructors on exception types. | Remove the obsolete serialization constructors. |
| `SYSLIB0027` / `SYSLIB0045` | Obsolete cryptographic factory / algorithm-by-name helpers. | Use the modern factory / named APIs. |
| `SYSLIB0057` | Obsolete `X509Certificate2(byte[])` constructor / `Import`. | Use `X509CertificateLoader`. |
| `SYSLIB0060` | Obsolete `Rfc2898DeriveBytes` constructors. | Use the static `Rfc2898DeriveBytes.Pbkdf2` method. |
| `CS8600`/`CS8601`/`CS8602`/`CS8603`/`CS8604` | Nullable reference annotation differences in the net10 BCL. | Fix the nullability annotations / flow. |
| `CS8621`/`CS8622`/`CS8714`/`CS8765`/`CS8767` | Nullability mismatches on delegates, overrides and interface members. | Align the nullability of the signatures. |
| `IDE0031` | "Null check can be simplified" style rule newly triggered on net10. | Apply the suggested simplification. |

## Phase 1b - single-target structural cleanup (DONE)

Phase 1b dropped `.NET Standard` entirely and removed everything that only existed to support it.

### Targeting

- [x] Both production libraries now single-target `net10.0` (`TargetFrameworks` collapsed to a single
	  `TargetFramework`): `Yubico.Core/src/Yubico.Core.csproj`, `Yubico.YubiKey/src/Yubico.YubiKey.csproj`.
- [x] Raised C# `LangVersion` from `13.0` to `14.0` in
	  [`build/CompilerSettings.props`](../build/CompilerSettings.props).

### Removed polyfills and compatibility packages

- [x] `Yubico.DotNetPolyfills` - not applicable: no such project exists in this fork (no directory, no
	  references), so there was nothing to delete.
- [x] Deleted the in-tree `CryptographicOperations` polyfill (previously at
	  `Yubico.Core/src/System.Security.Cryptography/CryptographicOperations.cs`); net10 uses the in-box
	  `System.Security.Cryptography.CryptographicOperations`.
- [x] Removed the `netstandard`-only item groups and their packages: `System.Memory`,
	  `Microsoft.Bcl.HashCode` (Yubico.Core) and `Microsoft.Bcl.AsyncInterfaces` (Yubico.YubiKey).
- [x] Removed `PolySharp` (both libraries) and `Nullable` (Yubico.YubiKey); their features are in-box on net10.
- [x] Pruned the packages flagged by `NU1510` (now provided in-box by the framework): `Microsoft.Win32.Registry`,
	  `System.Security.Principal.Windows`, `System.Text.Encoding.CodePages` (Yubico.Core), `System.Formats.Asn1`
	  (Yubico.YubiKey and `tests/integration`), and `System.Security.Principal.Windows` (`tests/sandbox`).
	  `System.Formats.Cbor` was kept - it is a standalone package, not in-box.
- [x] `NU1510` no longer fires, so it was removed from `WarningsNotAsErrors`.

### Collapsed conditional compilation

- [x] Removed the only `#if NETSTANDARD` block in source (`Yubico.Core/src/Yubico/Core/Tlv/TlvObject.cs`),
	  keeping the modern `StringComparison.Ordinal` branch. `#if NETFRAMEWORK` blocks were left intact (they
	  guard the `net472`-specific code paths and are independent of the netstandard removal).

## Follow-up - diagnostic remediation (TODO)

The structural cleanup intentionally left the net10 analyzer/obsolete diagnostics **demoted** (visible
warnings, not errors) via `WarningsNotAsErrors` in [`build/CompilerSettings.props`](../build/CompilerSettings.props).
Raising to C# 14 additionally surfaced `IDE0032`. Address each family below, then remove the corresponding
entry from `WarningsNotAsErrors` so the diagnostic is enforced as an error again. Several fixes (notably
`SYSLIB0057`/`SYSLIB0060`) change runtime behaviour in security-sensitive crypto code and should be reviewed
and tested in isolation. Known hotspots:

- [x] `SYSLIB0004` - `Yubico.Core/src/Yubico/PlatformInterop/Desktop/SCard/SCardCardHandle.cs`,
	  `.../SCardContext.cs`. Re-enabled as an error.
- [x] `SYSLIB0051` - `Yubico.Core/src/Yubico/PlatformInterop/PlatformApiException.cs`. Re-enabled as an error.
- [x] `SYSLIB0057` - `Yubico.YubiKey/src/Yubico/YubiKey/Scp/SecurityDomainSession.cs`,
	  `Yubico.YubiKey/src/Yubico/YubiKey/Piv/PivSession.KeyPairs.cs` and
	  `.../Piv/PivSession.Attestation.cs` (now use `X509CertificateLoader`). Re-enabled as an error.
- [x] `SYSLIB0060` - `Yubico.YubiKey/src/Yubico/YubiKey/Piv/PivSession.Pinonly.cs` (now uses `Rfc2898DeriveBytes.Pbkdf2`). Re-enabled as an error.
- [x] `SYSLIB0027` / `SYSLIB0045` - legacy cryptography factory / algorithm-by-name helpers.
	  `CryptographyProviders.HmacCreator` now maps algorithm names to non-obsolete HMAC constructors via a
	  private `CreateHmac` switch (public `Func<string, HMAC>` contract preserved); `PivSession.Attestation.cs`
	  uses `certificate.GetRSAPublicKey()` instead of the obsolete `PublicKey.Key`. Re-enabled as errors.
- [x] `CS86xx` nullable - **DONE.** All 71 unique sites fixed with real null-flow analysis (guards,
	  guarded locals, `?? throw`/`?? string.Empty`, `[MaybeNullWhen(false)]`, nullable-enum guards, widened
	  nullable params, and spreading the already-validated local rather than a re-copied field) - **not**
	  blanket `!`. Covered the Core HID/udev P/Invoke `string?`/`byte[]?` returns, the crypto
	  `Oid.Value`/`RSAParameters`/`ECPoint`/`HashAlgorithm.Hash` paths (`ECPublicKey.cs`, `RSAPublicKey.cs`,
	  `Asn*KeyEncoder`, `PinUvAuthProtocolOne`/`Two.cs`, `Scp11State.cs`), FIDO2/COSE, the Oath `Credential`
	  query parsing, and `YubiKeyDevice.Static.TryGetYubiKey`. One irreducible C#14 `field`-keyword getter
	  false positive in `PivSessionAttestTests.cs` was resolved with a single targeted null-forgiving op
	  (`DeviceMock!`). `CS8600;CS8601;CS8602;CS8603;CS8604;CS8621;CS8622;CS8714;CS8765;CS8767` and the comment
	  block were removed from `WarningsNotAsErrors`; re-enabled as errors and the full solution builds clean.
	  Validated by `Yubico.YubiKey.UnitTests` (3550 passed) and `Yubico.Core.UnitTests` (460 passed, 19
	  platform-skipped).
- [x] `IDE0031` - "null check can be simplified" - e.g. `Yubico.Core/src/Yubico/Core/Tlv/TlvWriter.cs`. Re-enabled as an error.
- [x] `IDE0032` - "use auto property" (newly triggered by C# 14's `field` keyword) - e.g.
	  `Yubico.Core/src/Yubico/Core/Logging/Log.cs`, `Yubico.Core/src/Yubico/Core/Iso7816/CommandApdu.cs`. Re-enabled as an error.
- [ ] Separately, the ~52 `CS0618` "obsolete member" warnings (not part of the demoted set, and **out of scope**
	  for this pass - they are the SDK's own `[Obsolete]` PIV key types / internal ml-dsa migration) can be
	  triaged here. Note: `CS0618` is currently *not* in `WarningsNotAsErrors`; it was confirmed to stay
	  non-build-breaking after the `CS86xx` family was re-enabled.
