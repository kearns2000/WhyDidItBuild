# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| main    | Yes       |

There is no numbered release yet. Security fixes land on `main`.

## Reporting a vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Report them privately with GitHub's [private vulnerability reporting](https://github.com/kearns2000/WhyDidItBuild/security/advisories/new).

Include:

- A description of the vulnerability and its potential impact
- Steps to reproduce
- Affected commit or version
- Any suggested fix or mitigation

You can expect an initial response within **72 hours**. We will work with you to understand and address the issue before any public disclosure.

## Scope

This policy covers the Why Did It Build? extension in this repository. The extension reads the Visual Studio Build output and an MSBuild log the user attaches, and it keeps that data on the machine. It does not cover Visual Studio, MSBuild, the .NET SDK, or the projects you choose to build.
