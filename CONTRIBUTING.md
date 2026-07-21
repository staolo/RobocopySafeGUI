# Contributing

Contributions should keep the application small, observable, and conservative around destructive file operations.

## Development Setup

- Windows 10 or 11
- .NET 10 SDK
- A working system `robocopy.exe`

```powershell
dotnet restore .\RobocopySafeGUI.sln
dotnet build .\RobocopySafeGUI.sln -c Release --no-restore
dotnet run --project .\tests\RobocopySafe.Harness\RobocopySafe.Harness.csproj -c Release --no-build
dotnet format .\RobocopySafeGUI.sln --verify-no-changes --no-restore
```

The harness changes attributes and creates junctions only inside `artifacts/`. Never point it at personal data.

## Change Requirements

- Preserve `ProcessStartInfo.ArgumentList`; do not build an executable command through shell concatenation.
- Add tests for path relationships, move cleanup, links, exclusions, or exit-code behavior when those areas change.
- Put user-visible text in `AppText` and update both English and Simplified Chinese catalogs.
- Keep Explorer integration per-user and avoid in-process Explorer extensions unless the security and maintenance tradeoff is explicitly reviewed.
- Avoid new runtime dependencies unless they remove more risk or complexity than they add.
- Do not commit logs, generated test trees, release archives, credentials, or machine-specific paths.

## Pull Requests

Explain the user-visible behavior, safety impact, tests run, and any compatibility tradeoff. Screenshots are useful for layout changes but must not expose private paths.
