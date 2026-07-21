# Security Policy

## Supported Versions

Security fixes are provided for the latest published release. Older releases may not receive backports.

## Reporting A Vulnerability

Use GitHub's private vulnerability reporting feature on this repository when available. Do not publish exploit details, sensitive local paths, or private file contents in a public issue.

Include:

- The affected version and Windows version.
- The operation and link-handling mode involved.
- A minimal reproduction using disposable directories.
- The expected and observed behavior.
- Whether source data, destination data, or paths outside the source tree were affected.

For ordinary correctness bugs without sensitive details, use the public bug-report template.

## Important Boundaries

- The application runs with the current user's permissions and is not a sandbox.
- Move mode can delete source files after successful copies.
- Follow-target mode can read outside the apparent source tree and is therefore blocked for moves.
- Logs may contain complete local file paths.
- The Explorer menu is registered only under `HKCU`; the application does not require elevation.
- Release executables are currently unsigned. Verify `SHA256SUMS.txt` and download only from this repository's Releases page.

See [`docs/SECURITY_MODEL.md`](docs/SECURITY_MODEL.md) for the detailed safety model.
