# Contributing to Why Did It Build?

Thanks for your interest in contributing. Why Did It Build? is a small Visual Studio extension that explains why an SDK-style project was rebuilt. Fixes, tests, and documentation that keep that focus are welcome.

## Before you start

- Search [existing issues](https://github.com/kearns2000/WhyDidItBuild/issues) to avoid duplicate work.
- For large changes (new outcomes, capture behaviour, public window changes), open an issue first to discuss approach.
- Keep pull requests focused. One feature or fix per PR is easier to review.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build the parser, the binlog reader, and the tests
- Visual Studio 2022 or 2026, with the .NET desktop development workload, to build the VSIX
- Any editor for the library and test projects

## Getting started

```bash
git clone https://github.com/kearns2000/WhyDidItBuild.git
cd WhyDidItBuild
dotnet test -c Release
```

The VSIX project builds on Windows:

```powershell
dotnet build WhyDidItBuild.sln -c Release
```

On other operating systems that project compiles a stub, because the Visual Studio SDK targets are Windows-only.

## Project layout

```text
src/WhyDidItBuild.Core/      # Outcomes, Fast Up-to-Date parsing, repeat comparison
src/WhyDidItBuild.Binlog/    # MSBuild binlog reader and correlation input
src/WhyDidItBuild/           # VSIX: capture, tool window, commands
tests/WhyDidItBuild.Tests/   # xUnit tests, including the sample binlog run
samples/RecurringBuild/      # Two SDK-style projects and a recurring trigger
```

Parsing and comparison have no Visual Studio dependency, so they can be tested on their own.

## Making changes

- Match existing naming and file structure.
- Use UK English in comments and documentation.
- Do not use em dashes in comments or documentation.
- Add or update tests for behaviour changes.
- Claim an outcome only when the captured evidence supports it. Missing diagnostics stay unknown.
- Keep the extension local. It does not send telemetry, start a build, or change the logging option.
- A binlog is optional. If a log cannot be tied to one project build, leave the original evidence in place.

## Testing expectations

All PRs should pass:

```bash
dotnet test -c Release
```

On Windows, also build the extension:

```powershell
dotnet build src/WhyDidItBuild/WhyDidItBuild.csproj -c Release
```

Add tests when you:

- Fix a parser or correlation bug
- Change an outcome, a repeat key, or a reason label
- Change how a binlog is matched to a captured build

## Pull request checklist

- [ ] `dotnet test -c Release` passes
- [ ] Tests added or updated for the change
- [ ] README updated if the window, the outcomes, or the setup steps changed
- [ ] No unrelated formatting or drive-by refactors

## Code of conduct

This project follows the [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold it.

## Licence

By contributing, you agree that your contributions will be licensed under the [MIT Licence](LICENSE).

## Questions

Open a [GitHub issue](https://github.com/kearns2000/WhyDidItBuild/issues) for questions or ideas. For security vulnerabilities, see [SECURITY.md](SECURITY.md).
