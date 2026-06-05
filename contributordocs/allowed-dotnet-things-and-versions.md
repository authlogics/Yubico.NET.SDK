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

# What .NET things/versions can we use?

What we think of as .NET is actually a collection of technologies. Thus, when we talk about the versions of
.NET, we are really talking about three separate things:

1. The C# language version - This dictates what language features and syntactic sugars we can leverage while
   writing code.

2. The Base Class Library (BCL) version - This dictates what .NET APIs we can call.

3. The build system / .NET SDK version - This dictates what build and project file improvements we can use
   when integrating with the `dotnet` tool, as well as static analyzers.

The specific versions discussed here apply to the SDK proper. Things like example code, demo apps, etc. can
use different / newer implementations of .NET, so long as they run on .NET 10.

## Base Class Library (BCL)

The BCL is the standard library for .NET. It is everything that you find under the `System.*` namespaces.
**The .NET SDK project targets .NET 10.** See the below sections for an explanation of what this means and
why we chose this.

To see whether we can use a particular API, look toward the bottom of the page for the "Applies to" section.
Note that if a method has multiple overloads, there will be multiple of these boxes. You may need to expand
the drop down (the icon next to .NET 5.0 and other versions) to see the full table.

If you see .NET 10 (or any version .NET 10 is built on) listed on the table, you're good to go.

![Microsoft Docs - Applies to which versions of .NET](./images/msft-docs-applies-to-version.png)

### History: why we used to target .NET Standard 2.0

This project originally targeted .NET Standard 2.0 so that a single build could be consumed by .NET Framework,
.NET Core, Xamarin/Mono and Unity. That breadth came at a cost: many modern BCL APIs were unavailable, and the
gaps had to be filled with [polyfills](./polyfills.md) and compatibility packages (`System.Memory`,
`Microsoft.Bcl.HashCode`, `Microsoft.Bcl.AsyncInterfaces`, `PolySharp`, `Nullable`, and an in-tree
`CryptographicOperations`).

This fork no longer needs that reach. It decoupled from upstream and targets a single, controlled runtime, so
it moved to **.NET 10 only**. The compatibility packages and polyfills were removed as part of that move - see
[`net10-migration.md`](./net10-migration.md) for the full history.

### Why .NET 10?

The target is dictated by the runtime this fork is deployed onto, which is .NET 10. Single-targeting a current
.NET keeps the codebase simple (no multi-target conditionals, no polyfills), gives us the full modern BCL, and
lets us use the latest language and analyzer features.

## C# language

Discussing the C# language version is considerably less complex than the BCL.

### We target C# language 14.0

You can see various proposals and additions that were made to the C# language
[here](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/). We support everything in the base
specification up to and including the C# 14.0 specification, which is the language version that ships with the
.NET 10 SDK.

Now that we single-target .NET 10, the language version is no longer constrained by .NET Standard, and we no
longer need polyfills for the BCL types that newer language features depend on.

## Build system / SDK version - .NET 10.0.x

This project depends on the .NET 10 SDK. The exact SDK version is pinned in [`global.json`](../global.json)
(`rollForward: latestFeature`), and CI provisions the same SDK via `global-json-file`.

In order to develop .NET libraries and applications, you need to install the .NET SDK. This is automatically
done for you if you select the ".NET workload" inside of the Visual Studio installer. You can also download
the .NET SDK as a separate standalone package.

The .NET SDK is what includes all of the compilers and build tools. This includes things like the `dotnet`
CLI tool, and the MSBuild build system.

Here, it is mostly safe to keep your system up to date and install newer .NET SDK versions. This is because
all new versions of the .NET SDK should (in theory) be retaining full backward compatibility with all previous
versions. Things like the language and BCL versions are locked in separately in the project files - the SDK
is responsible for honoring those.

Updating the SDK version allows us to leverage the latest and greatest tools, like the latest static analyzers
and code refactoring tools.

SDKs can also be installed side-by-side so that you can experiment with new features while maintaining a stable
daily build environment. How to achieve this is outside the scope of this document.
