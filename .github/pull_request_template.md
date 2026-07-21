## Summary

Describe the behavior changed and why.

## Safety Impact

Describe any effect on move deletion, links, exclusions, path validation, Explorer integration, or logging. Write `None` when not applicable.

## Verification

- [ ] `dotnet build .\RobocopySafeGUI.sln -c Release`
- [ ] Integration harness passes
- [ ] `dotnet format .\RobocopySafeGUI.sln --verify-no-changes`
- [ ] English and Simplified Chinese UI text updated when applicable
- [ ] Screenshots contain no private paths or data
