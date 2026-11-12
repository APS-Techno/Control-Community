# Third-party notices

APS-owned material in this source distribution is licensed under Apache-2.0; see [LICENSE](LICENSE) and [NOTICE](NOTICE). The packages below retain their original licenses, copyright and attribution. The APS license does not supersede those separate grants. Licenses are taken from each package's nuspec (`licenseExpression`, or `licenseUrl` where no expression is given).

## NuGet packages that ship in `bin`

| Package | Version | License |
|---|---|---|
| ScottPlot | 5.0.54 | MIT |
| ScottPlot.WinForms | 5.0.54 | MIT |
| SkiaSharp | 2.88.9 | MIT |
| SkiaSharp.HarfBuzz | 2.88.9 | MIT |
| SkiaSharp.Views.Desktop.Common | 2.88.9 | MIT |
| SkiaSharp.Views.WindowsForms | 2.88.9 | MIT |
| SkiaSharp.NativeAssets.Win32 | 2.88.9 | MIT |
| SkiaSharp.NativeAssets.Linux.NoDependencies | 2.88.9 | MIT |
| SkiaSharp.NativeAssets.macOS | 2.88.9 | MIT |
| HarfBuzzSharp | 7.3.0.3 | MIT |
| HarfBuzzSharp.NativeAssets.Win32 | 7.3.0.3 | MIT |
| HarfBuzzSharp.NativeAssets.Linux | 7.3.0.3 | MIT |
| HarfBuzzSharp.NativeAssets.macOS | 7.3.0.3 | MIT |
| OpenTK | 3.1.0 | The Open Toolkit license (MIT/X11 style); `licenseUrl` http://github.com/opentk/opentk/blob/master/License.txt; Copyright (c) 2006 - 2019 Stefanos Apostolopoulos for the Open Toolkit library |
| OpenTK.GLControl | 3.1.0 | The Open Toolkit license (as OpenTK) |
| System.IO.Ports | 9.0.14 | MIT |
| runtime.\*.runtime.native.System.IO.Ports (all runtime identifiers) | 9.0.14 | MIT |
| Sharp7 | 1.1.84 | MIT (`licenseUrl` https://raw.githubusercontent.com/fbarresi/Sharp7/master/LICENSE; Copyright (c) 2018 Federico Barresi) |

System.Drawing.Common 4.7.3, Microsoft.Win32.SystemEvents 4.7.0, Microsoft.NETCore.Platforms 3.1.9 and System.Security.Cryptography.ProtectedData 9.0.0 resolve at restore but the .NET runtime supplies them. They are not copied into `bin` and are not distributed here.

## Test-only packages (not shipped in an application)

| Package | Version | License |
|---|---|---|
| xunit, xunit.core, xunit.assert, xunit.extensibility.\* | 2.9.3 / 2.9.2 (Icons.Tests, VacuumControls.Tests) | Apache-2.0 |
| xunit.abstractions | 2.0.3 | Apache-2.0 |
| xunit.analyzers | 1.18.0 / 1.16.0 (Icons.Tests, VacuumControls.Tests) | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 / 2.8.2 | Apache-2.0 |
| Xunit.SkippableFact | 1.5.23 | MS-PL |
| Xunit.StaFact | 1.1.11 | MS-PL |
| Validation | 2.5.51 | MS-PL |
| Microsoft.NET.Test.Sdk, Microsoft.TestPlatform.\*, Microsoft.CodeCoverage | 17.14.1 / 17.11.1 | MIT |
| coverlet.collector | 6.0.4 | MIT |
| Newtonsoft.Json | 13.0.3 / 13.0.1 | MIT |
| System.Collections.Immutable, System.Reflection.Metadata | 8.0.0 / 1.6.0 | MIT |

## Packages used by auxiliary tools (not copied into `bin`)

| Component | Packages | License |
|---|---|---|
| Companion MCP server (`Mcp`) | ModelContextProtocol 2.2.0 | Apache-2.0 |
| | Microsoft.Extensions.\* and System.\* 10.0.x (transitive) | MIT |
| Icon generator (`Icons/Generator`) | Svg.Skia 1.0.0.16 and its transitive packages, SkiaSharp 2.88.8 | MIT |

## Artwork and fonts

The SVG icons in `Icons/svg` are APS-authored. The Digital-7 Mono display font is NOT bundled; it is referenced by name only and is used if the operating system has it installed. No font files are distributed.
