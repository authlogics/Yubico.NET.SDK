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

Phase 1 adds `net10.0` alongside the existing `.NET Standard` targets so that nothing breaks for current
consumers while net10 support is brought online.

- `global.json` SDK pinned to the .NET 10 SDK (`10.0.300`, `rollForward: latestFeature`).
- The two production libraries multi-target `netstandard2.0;netstandard2.1;net10.0`:
	- `Yubico.Core/src/Yubico.Core.csproj`
	- `Yubico.YubiKey/src/Yubico.YubiKey.csproj`
- Compatibility packages that are only needed on `.NET Standard` were scoped behind a
  `Condition="$(TargetFramework.StartsWith('netstandard'))"` item group so they are not referenced on net10:
	- `Yubico.Core`: `System.Memory`, `Microsoft.Bcl.HashCode`
	- `Yubico.YubiKey`: `Microsoft.Bcl.AsyncInterfaces`
- The in-tree `CryptographicOperations` polyfill
  (`Yubico.Core/src/System.Security.Cryptography/CryptographicOperations.cs`) is now guarded with
  `#if NETSTANDARD2_0` (the class is only compiled for netstandard2.0) and forwards to the in-box type via
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

## Phase 1b - single-target cleanup (TODO)

Phase 1b drops `.NET Standard` entirely and removes everything that only existed to support it. None of the
items below were done in Phase 1.

### Targeting

- [ ] Change both production libraries to single-target `net10.0` (remove `netstandard2.0` and `netstandard2.1`
	  from `TargetFrameworks`).
- [ ] Raise C# `LangVersion` from `13.0` to `14.0` in [`build/CompilerSettings.props`](../build/CompilerSettings.props).

### Remove polyfills and compatibility packages

- [ ] Delete the `Yubico.DotNetPolyfills` project (`Yubico.DotNetPolyfills/src`) and all references to it.
- [ ] Delete the in-tree `CryptographicOperations` polyfill
	  (`Yubico.Core/src/System.Security.Cryptography/CryptographicOperations.cs`).
- [ ] Remove the `netstandard`-only item groups and the packages they reference: `System.Memory`,
	  `Microsoft.Bcl.HashCode` (Yubico.Core) and `Microsoft.Bcl.AsyncInterfaces` (Yubico.YubiKey).
- [ ] Remove `PolySharp` and `Nullable` package references (their features are in-box on net10).
- [ ] Prune the packages flagged by `NU1510`: `System.Formats.Asn1`
	  (`Yubico.YubiKey/tests/integration`) and `System.Security.Principal.Windows`
	  (`Yubico.YubiKey/tests/sandbox`). Re-evaluate `System.Formats.Cbor` on the production libraries.
- [ ] Once `NU1510` no longer fires, remove it from `WarningsNotAsErrors`.

### Collapse conditional compilation

- [ ] Remove `#if NETSTANDARD` / `#if NETSTANDARD2_0` / `#if NETSTANDARD2_1_OR_GREATER` blocks now that only
	  one target remains.

### Properly fix the demoted diagnostics, then re-enable errors

Address each family from the table above and then remove the corresponding entry from `WarningsNotAsErrors`
so the diagnostics are enforced as errors again. Known hotspots observed during the Phase 1 build:

- [ ] `SYSLIB0004` - `Yubico.Core/src/Yubico/PlatformInterop/Desktop/SCard/SCardCardHandle.cs`,
	  `.../SCardContext.cs`.
- [ ] `SYSLIB0051` - `Yubico.Core/src/Yubico/PlatformInterop/PlatformApiException.cs`.
- [ ] `SYSLIB0057` - `Yubico.YubiKey/src/Yubico/YubiKey/Scp/SecurityDomainSession.cs`,
	  `Yubico.YubiKey/src/Yubico/YubiKey/Piv/PivSession.KeyPairs.cs`.
- [ ] `SYSLIB0060` - `Yubico.YubiKey/src/Yubico/YubiKey/Piv/PivSession.Pinonly.cs`.
- [ ] `CS86xx` nullable - e.g. `Yubico.Core/.../Hid/MacOSHidIOReportConnection.cs`,
	  `Yubico.Core/.../Linux/Udev/LinuxUdevScan.cs`,
	  `Yubico.YubiKey/src/Yubico/YubiKey/Otp/Operations/CalculateChallengeResponse.cs`,
	  `Yubico.YubiKey/src/Yubico/YubiKey/YubiKeyDevice.Static.cs`.
- [ ] `IDE0031` - e.g. `Yubico.Core/src/Yubico/Core/Tlv/TlvWriter.cs`.

### Documentation

- [ ] Refresh [`allowed-dotnet-things-and-versions.md`](./allowed-dotnet-things-and-versions.md) to describe the
	  net10-only posture (BCL = .NET 10, C# 14.0, SDK 10.0.x) instead of .NET Standard 2.0 / C# 8.0 / .NET 5.
- [ ] Add or refresh `polyfills.md` (currently linked from the contributor docs index but missing); after
	  Phase 1b it should explain that polyfills are no longer required.
