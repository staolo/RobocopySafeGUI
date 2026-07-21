# Security Model

Robocopy Safe Copy reduces common accidental-copy hazards while keeping Robocopy as the data-plane engine. It is not a sandbox, backup verifier, or privilege boundary.

## Trust Boundaries

| Component | Role | Trust assumption |
| --- | --- | --- |
| GUI | Validates settings, builds arguments, displays progress | Runs as the current user |
| `robocopy.exe` | Reads, writes, and optionally removes data | Trusted Windows system binary |
| Source/destination paths | User-selected file trees | May contain links, protected items, long paths, or locked files |
| Explorer clipboard | Stages one directory and copy/move intent | May change between actions |
| Registry | Stores classic context-menu verbs under `HKCU` | Writable by the current user |
| Logs | Preserve meaningful Robocopy output | May disclose local paths |

## Enforced Invariants

1. Source and destination cannot resolve to the same directory.
2. Destination cannot be inside the source directory, including through existing directory links or junction aliases.
3. Move operations cannot follow link targets.
4. A source root that is itself a reparse point is rejected unless copy + follow-target is explicitly selected.
5. Default link handling adds `/XJ /XJD /XJF` and the exclusion scan does not recurse into reparse points.
6. Custom exclusion values beginning with `/` are rejected so they cannot become Robocopy switches.
7. Robocopy arguments use `ProcessStartInfo.ArgumentList`, not a shell command string.
8. Exit codes `0-7` are treated as non-failure; codes `8+` are failures.
9. A successful move is followed by a source inspection. The clipboard is cleared only when the source is gone or complete according to the finalizer.
10. Explorer integration is static and per-user; no third-party DLL is loaded into Explorer.

## Explicit Risk Choices

- Disabling hidden/system exclusions may copy protected or normally invisible data.
- Copying link nodes preserves reparse points at the destination.
- Following link targets may read data outside the visible source tree; it is copy-only and requires explicit selection.
- Move mode uses `/MOVE /IS /IT /IM` so source files that match existing destination entries are still processed and can be removed after successful handling.
- The application uses `/COPY:DAT /DCOPY:DAT`; it does not promise ACL, owner, or audit metadata preservation.

## Non-Goals

- Defending against a malicious user who already has the same Windows account permissions.
- Transactional rollback after hardware failure, process termination, or filesystem corruption.
- Cryptographic verification of copied file contents.
- Backup retention, versioning, synchronization, or destination mirroring.
- Processing arbitrary individual-file or multi-item queues from the context menu.

Use disposable test directories for new link, move, and exclusion behavior. Use Preview before applying unfamiliar settings to important data.
