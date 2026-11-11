# Third-Party Notices and Licenses

Radpa bundles or links the following open-source software and font assets. Full license notices appear below.

---

## 1. Embedded Font Asset: Vazirmatn UI (`v33.003`)

Files embedded as Avalonia resources in `src/Radpa.UI/Assets/Fonts/`:
- `Vazirmatn-UI-Regular.ttf`
- `Vazirmatn-UI-Medium.ttf`
- `Vazirmatn-UI-Bold.ttf`

(Subsetted cleanly with `fonttools pyftsubset` retaining Persian/Arabic shaping and Latin/digits Unicode ranges along with original OFL metadata tables.)

```text
Copyright 2015 The Vazirmatn Project Authors (https://github.com/rastikerdar/vazirmatn)

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
http://scripts.sil.org/OFL


-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded, 
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, modify,
redistributed and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to remain
under this license does not apply to any document created using the fonts
or their derivatives.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE SOFTWARE.
```

---

## 2. NuGet Package Dependencies (`Directory.Packages.props`)

| Package | Version | License | Purpose |
|---|---:|---|---|
| `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` | `12.1.6` | MIT | Cross-platform UI & Fluent dark/light styling |
| `Avalonia.Controls.DataGrid` | `12.1.2` | MIT | Sortable application and audit tables |
| `Avalonia.Headless.XUnit` | `12.1.6` | MIT | Automated headless UI & RTL verification |
| `CommunityToolkit.Mvvm` | `8.4.2` | MIT | AOT-safe source-generated MVVM observables & commands |
| `Microsoft.Data.Sqlite` | `10.0.15` | MIT | Plain ADO.NET SQLite driver (`e_sqlite3` bundled) |
| `Microsoft.Diagnostics.Tracing.TraceEvent` | `4.0.0` | MIT | Windows Kernel-Network ETW collector (in `Radpa.NetCollector`) |
| `System.Security.Cryptography.ProtectedData` | `10.0.15` | MIT | Windows DPAPI key protection |
| `Microsoft.CodeAnalysis.NetAnalyzers` | `10.0.404` | MIT | Static code analysis (build-time only) |
| `Roslynator.Analyzers` | `5.0.1` | Apache-2.0 | Code quality analyzer (build-time only) |
| `Microsoft.NET.Test.Sdk` | `18.11.1` | MIT | Test host (test projects only) |
| `xunit.v3` | `3.2.2` | Apache-2.0 | Unit & headless test framework (test projects only) |
| `xunit.runner.visualstudio` | `3.1.5` | Apache-2.0 | Test adapter (test projects only) |
| `FluentAssertions` | `7.2.0` | Apache-2.0 | Readable test assertions (test projects only) |
| `NSubstitute` | `6.2.0` | BSD-3-Clause | Interface mocking in tests (test projects only) |
| `BenchmarkDotNet` | `0.15.8` | MIT | Hot-path microbenchmarks (benchmark project only) |
| `coverlet.collector` / `coverlet.msbuild` | `10.1.0` | MIT | Line coverage instrumentation & threshold gates |
