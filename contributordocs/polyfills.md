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

# Polyfills

A *polyfill* is a small piece of code that provides an API on a target framework that does not ship that API
natively, so that newer source code can compile and run on an older runtime.

## We no longer use polyfills

This fork single-targets **.NET 10**. The full modern Base Class Library and the C# 14 language features are
available in-box, so there is nothing to backfill. As part of the
[.NET 10 migration](./net10-migration.md), all polyfills and compatibility shims were removed:

- The in-tree `CryptographicOperations` polyfill (previously under
  `Yubico.Core/src/System.Security.Cryptography/`) was deleted; the in-box
  `System.Security.Cryptography.CryptographicOperations` is used instead.
- The [`PolySharp`](https://github.com/Sergio0694/PolySharp) source generator and the
  [`Nullable`](https://github.com/manuelroemer/Nullable) attribute package were removed.
- The BCL compatibility packages `System.Memory`, `Microsoft.Bcl.HashCode` and
  `Microsoft.Bcl.AsyncInterfaces` were removed (their types - `Span<T>`/`Memory<T>`, `HashCode`,
  `IAsyncEnumerable<T>`/`IAsyncDisposable` - are in-box on .NET 10).

## Why this matters

Historically the SDK targeted .NET Standard 2.0 to reach the widest possible audience (see
[What .NET things/versions can we use?](./allowed-dotnet-things-and-versions.md)). On that target many modern
APIs were missing, and polyfills filled the gap - but they needed careful testing to make sure the shimmed
behaviour matched the real BCL.

Single-targeting .NET 10 removes that burden entirely. **New code should call BCL APIs directly. Do not add
polyfills or compatibility packages.** If an API you need is not available on .NET 10, that is a signal to use
a different API, not to introduce a shim.
