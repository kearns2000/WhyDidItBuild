# Why Did It Build?

[![Visual Studio](https://img.shields.io/badge/Visual%20Studio-2022%20%7C%202026-5C2D91?style=flat&logo=visualstudio&logoColor=white)](https://visualstudio.microsoft.com/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Build](https://github.com/kearns2000/WhyDidItBuild/actions/workflows/ci.yml/badge.svg)](https://github.com/kearns2000/WhyDidItBuild/actions/workflows/ci.yml)
[![Licence: MIT](https://img.shields.io/badge/Licence-MIT-blue?style=flat)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-xUnit-5C2D91?style=flat&logo=xunit)](tests/WhyDidItBuild.Tests)

**Visual Studio:** 2022 and 2026 · **Libraries:** `net10.0` · **Language:** C# · **Test runner:** xUnit

A Visual Studio extension that answers a question that comes up in large .NET solutions: a build or F5 ran again, and it is not obvious why Visual Studio sent a project to MSBuild.

The window shows the latest completed build and the previous comparable build. Each row is a project (and target framework, when the diagnostics name one) with an outcome, the reason taken from the Fast Up-to-Date Check, and whether that same reason appeared in both builds.

## What it can and cannot claim

The Fast Up-to-Date Check writes lines prefixed with `FastUpToDate:` into the Build output. Those lines explain why Visual Studio considered a project up to date, or why it sent the project to MSBuild. A line such as `GeneratedRoutes.cs was newer than the last build output` is the reason the project was sent to MSBuild. It does not prove that the C# compiler ran.

| Outcome | What the evidence shows |
| --- | --- |
| Up to date | A `FastUpToDate` line says the project is up to date. |
| Sent to MSBuild | The up-to-date check failed the project, and there is no correlated MSBuild log that shows what the compiler did. |
| Compilation ran | A correlated MSBuild log contains a `Csc` task for that project. |
| Compilation skipped | A correlated MSBuild log explicitly skips `CoreCompile` for that project. |
| Unknown | The captured diagnostics do not decide the outcome. Missing diagnostics are never treated as up to date. |

`Repeated in 2 builds` means the same project and build configuration reported the same reason and the same relevant file in the last two comparable builds. Timestamps are ignored. Clean, Rebuild, and design-time builds are shown when they happen and are left out of that comparison. The window does not claim that source files were unchanged unless a diagnostic says so.

## Build the VSIX

Build on Windows, with Visual Studio 2022 or 2026 and the .NET desktop development workload. The extension project targets .NET Framework 4.7.2 and the Visual Studio 17.x SDK.

```powershell
dotnet build WhyDidItBuild.sln -c Release
```

The package is:

```text
src\WhyDidItBuild\bin\Release\WhyDidItBuild.vsix
```

This repository was developed on macOS. `dotnet build` here compiles the parser, the binlog reader, and the tests. The VSIX project compiles only a stub on non-Windows, because the Visual Studio SDK targets are Windows-only. The `.vsix` file has to be produced on Windows.

### Compatibility

Checked against current Visual Studio guidance before the project was written:

- `Microsoft.VisualStudio.SDK` 17.14.40265
- `Microsoft.VSSDK.BuildTools` 17.14.2142
- Installation target `Microsoft.VisualStudio.Community` version `[17.0,)`, for `amd64` and `arm64`
- Prerequisite `Microsoft.VisualStudio.Component.CoreEditor` `[17.0,)`

Visual Studio 2026 loads API 17.x extensions and evaluates only the lower bound of `InstallationTarget`. The project does not reference the 18.x SDK, so the same VSIX installs on Visual Studio 2022 and Visual Studio 2026.

## Install

1. Close every Visual Studio instance.
2. Double-click `WhyDidItBuild.vsix`, or run `VSIXInstaller.exe` on that file.
3. Confirm the install targets Visual Studio 2022 and, if it is installed, Visual Studio 2026.
4. Start Visual Studio and open a solution. The package loads in the background when a solution exists.

To debug the extension, open `WhyDidItBuild.sln` in Visual Studio and press F5. The project starts the Experimental Instance (`devenv.exe /rootsuffix Exp`).

## Turn on up-to-date logging

The extension reads the Build output. Visual Studio writes Fast Up-to-Date Check diagnostics only when logging is enabled.

1. Open **Tools > Options > Projects and Solutions > SDK-Style Projects**.
2. Set **Logging Level** to **Info**.
3. Build again. The extension does not change this option and does not start a build.

**Minimal** records out-of-date projects only, so a second build cannot show a project as up to date. **Info** also records `Project is up-to-date.` **Verbose** adds the input and output lists.

The same instruction is shown in the tool window when a build was captured and those diagnostics are missing.

### Manual fallback

**Import Build Output** pastes or opens a saved Build output log. Parsing is the same code used for a live capture. If the text contains more than one `==========` summary, only the last build is used. An imported log has no capture time, so a later MSBuild log is applied only when exactly one project build matches.

## Use the window

1. Open an SDK-style .NET solution.
2. Build Solution, or start debugging with F5.
3. Open **View > Other Windows > Why Did It Build?** for the whole solution.
4. Or right-click one project in Solution Explorer and choose **Explain Last Build**. That opens the same window and selects the project.

The table columns are **Project**, **Outcome**, **Reason**, and **Repeated**. The previous comparable build is listed under the table. Selecting a row shows the original lines. A path in those lines is a link when the file exists.

**Attach MSBuild Log** reads a `.binlog` you already have. It does not start a build. The log is merged only when the project path, configuration, target framework, and capture time identify one result. A target framework named by the up-to-date check is matched only when the log names that same framework. A `CoreCompile` skip that says the target was previously built does not cancel a `Csc` task in the same build. If a real skip and a compiler task disagree, the lines stay on that project and the outcome is left unchanged. The window works without a binlog. If another build finishes while the log is being read, the log is not applied to the older build.

Visual Studio does not write a binlog unless you ask MSBuild to. For the build you are about to run, set these environment variables before starting Visual Studio, then build once:

```text
MSBuildDebugEngine=1
MSBUILDDEBUGPATH=C:\temp\vs-binlogs
```

Attach the `.binlog` from that directory to the build the window already captured. A `dotnet build -bl` log from another process will not line up with a Visual Studio capture unless its timestamps fall inside that capture.

## Two-build demo

`samples/RecurringBuild` contains two SDK-style projects, `Library` and `App`. `App` references `Library`. After each real build, a target rewrites `App/obj/GeneratedRoutes.cs`. That file is an up-to-date input and is outside the compile items, so the project sources stay unmodified. The next Fast Up-to-Date Check sees it as newer than the build outputs and sends `App` to MSBuild. MSBuild then skips `CoreCompile`, because the file is not a compiler input. Design-time builds do not run the target.

The sample targets `net10.0` so it builds on the machine used to write this repository. Change `TargetFramework` in both projects if you want a different runtime.

1. Open `samples/RecurringBuild/RecurringBuild.sln` in Visual Studio, with SDK-style logging set to **Info**.
2. Build the solution. Both projects do work. `App` is out of date because an output is missing or this is the first check. That reason is not the one later builds repeat.
3. Build again without editing source. `Library` can show **Up to date** when the check logs that decision. `App` shows **Sent to MSBuild**, with `GeneratedRoutes.cs was newer than the last build output`. Compilation stays unknown until you attach a binlog.
4. Build a third time. `App` now has the same reason and the same file as the previous comparable build, so the Repeated column says **Repeated in 2 builds**.

To see the compiler distinction, produce a binlog for the second or third Visual Studio build and use **Attach MSBuild Log**. `Library` stays **Up to date** (a compiler skip is not merged into an up-to-date result). `App` becomes **Compilation skipped** when the log contains an explicit `CoreCompile` skip, and the reason stays the up-to-date explanation. A first build, where `Csc` runs, shows **Compilation ran**.

## What this machine verified

The development machine is macOS. It cannot launch the Visual Studio Experimental Instance, and `dotnet build` of the VSIX project here compiles a stub assembly rather than a `.vsix`.

`dotnet test` passed: 28 tests. They cover the parser, the repeat comparison, and binlog correlation, including two real `dotnet build -bl` runs of the sample:

- The first binlog contains a `Csc` task for both projects.
- The second binlog skips `CoreCompile` for both and does not run `Csc`.
- A synthetic Visual Studio capture in that second time window keeps `Library` up to date and marks `App` as compilation skipped.
- A second build's up-to-date line is honored even when the Build output also says the build started.
- Missing `FastUpToDate` lines stay **Unknown**.
- The same reason and file across two comparable builds is labeled **Repeated in 2 builds**. Clean, Rebuild, design-time, and cancelled builds are excluded.
- Ambiguous or out-of-window binlog entries are not merged.

## Manual checks still required in Visual Studio

On a Windows machine with Visual Studio 2022 or 2026:

1. Build `WhyDidItBuild.sln` in Release and install the VSIX, or press F5 to open the Experimental Instance.
2. Open `samples/RecurringBuild/RecurringBuild.sln` in that instance. Set SDK-style logging to **Info**.
3. Build once and confirm both projects appear with a reason, and neither is marked up to date unless a `FastUpToDate` line says so.
4. Build a second time and confirm `Library` is **Up to date** when the Build output contains `Project is up-to-date. (Library)`.
5. Build a third time and confirm `App` is **Repeated in 2 builds**.
6. Set logging to **None**, build again, and confirm the window says the diagnostics are missing and does not mark projects up to date.
7. Right-click `App` and choose **Explain Last Build**. Confirm the window opens and `App` is selected. Open **View > Other Windows > Why Did It Build?** and confirm it shows the whole solution.
8. Switch between the light and dark themes and confirm the window follows the theme.
9. With `MSBuildDebugEngine=1` and `MSBUILDDEBUGPATH` set before Visual Studio starts, build, then attach that binlog. Confirm a project whose log contains `Csc` shows **Compilation ran**, and `App` on a later build shows **Compilation skipped**.
10. Start debugging with F5 once and confirm that launch is captured as the build that F5 triggered, without the extension starting a second build.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, project layout, and pull request expectations. This project follows the [Code of Conduct](CODE_OF_CONDUCT.md). Security reports go through [SECURITY.md](SECURITY.md).

## Licence

MIT. See [LICENSE](LICENSE).
