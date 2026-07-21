# Changelog

All notable changes to this project are documented here.

## [1.0.0] - 2026-07-22

### Added

- Copy, move, and Robocopy `/L` preview workflows.
- Conservative hidden/system item and link handling.
- Per-user classic Explorer context menu for copy/cut/paste-style folder workflows.
- Batched progress and bounded visible logging with full logs under Local AppData.
- English and Simplified Chinese UI with persisted language selection.
- Self-contained x64 and ARM64 release packaging with SHA-256 checksums.

### Safety

- Reject destination paths inside the source tree, including paths routed back through existing junctions.
- Reject source-root links unless copy + follow-target is explicitly selected.
- Block follow-target mode for moves.
- Reject option-like custom exclusions.
- Verify move-source cleanup before clearing the clipboard.
