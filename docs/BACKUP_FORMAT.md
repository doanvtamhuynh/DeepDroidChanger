# Device backup format

The current backup writer stores a versioned ZIP directly in the `.ddcbak`
file. The archive is intentionally passwordless; callers must protect the
file and its copies because it can contain device identity and account data.

## Current `.ddcbak` format, version 3

The file is a normal ZIP archive beginning with a ZIP signature. It contains:

- `manifest.json`
- `properties.json` and `settings.json` when selected
- `apps/<package>/...` and `google/<package>/...` payloads
- `optional/keybox.xml`
- `experimental/settings_ssaid.xml`
- `account/...` Google Account State snapshots

The root manifest records `FormatVersion = 3`, `Encrypted = false`, and
`EncryptionFormatVersion = 0`. Internal SHA-256 checksums remain mandatory for
all payload entries; the structural root and per-package manifests are not
checksum entries. ZIP paths, duplicates, payload types, and checksums are
validated before restore mutation.

The writer creates and validates a temporary ZIP, copies it to a unique
`.ddcbak.partial` file, validates that direct ZIP again, and atomically renames
the partial file to the final `.ddcbak` name. Temporary payload files and the
partial file are removed on failure or cancellation.

## Legacy `DDCBACKUP` envelope, previous format

Older releases wrote a password-protected outer envelope with this layout:

All integer fields are little-endian. The envelope is laid out in this order:

| Field | Size | Value |
| --- | ---: | --- |
| Magic | 9 bytes | ASCII `DDCBACKUP` |
| Envelope format version | 1 byte | `1` |
| KDF ID | 1 byte | `1` = PBKDF2-HMAC-SHA256 |
| Encryption algorithm ID | 1 byte | `1` = AES-256-CBC with PKCS#7 |
| Authentication algorithm ID | 1 byte | `1` = HMAC-SHA256 |
| PBKDF2 iterations | 4 bytes | `600000` |
| Salt length | 4 bytes | `16` |
| IV length | 4 bytes | `16` |
| Ciphertext length | 8 bytes | Length of the padded ciphertext |
| Salt | 16 bytes | Cryptographically random per backup |
| IV | 16 bytes | Cryptographically random per backup |
| Ciphertext | Variable | AES-256-CBC encrypted ZIP payload |
| HMAC-SHA256 | 32 bytes | MAC over the complete header and ciphertext |

The fixed header (through the IV) is 65 bytes. The HMAC covers those 65 header
bytes followed by exactly the declared ciphertext length; the HMAC itself is
not included in its input.

PBKDF2 derives 64 bytes from the UTF-8 password and salt. The first 32 bytes
are the AES encryption key; the second 32 bytes are the HMAC key. The password,
derived keys, and account contents are not written to the envelope, manifest,
settings, or logs.

This format is retained only for identification and documentation. Password
entry is not part of the current Backup or Restore UI, and a legacy file is
rejected with
`legacy_encrypted_backup_not_supported_without_password` rather than being
reported as a corrupt ZIP.

## Google Account State

Google Account State is experimental, always emits the
`google_account_state_same_device_restore_only` warning, and keeps
`consistencyGuaranteed: false` on the summary. A fully successful summary uses
the `same_device_restore_only` reason; unavailable and partial summaries use
more specific safety reasons. Database components are handled independently:

- `/data/system_ce/0/accounts_ce.db*`
- `/data/system_de/0/accounts_de.db*`
- `/data/system/users/0/accounts.db*`
- `/data/system/syncmanager.db*`

For each existing database, the writer checks for `sqlite3`, runs SQLite
`.backup` to a device-temporary snapshot, verifies that the output exists and
has a positive size, then runs `PRAGMA quick_check` and requires `ok`. A
successful component is reported as `backed_up` with
`snapshotMethod: "sqlite_backup"` and `consistencyGuaranteed: true`.

If the source is absent, that component is `skipped_missing`. If the source
exists but `sqlite3`, `.backup`, snapshot creation, size validation, or
`quick_check` cannot complete, the component is
`snapshot_unavailable` with its specific reason and
`snapshotMethod: "unavailable"`. The live database is never raw-tarred as a
fallback. The manifest adds `google_account_state_snapshot_unavailable` once
when needed.

The non-database paths `/data/system/sync` and
`/data/system/users/0/registered_services` may be copied as raw tar payloads;
they are reported with `snapshotMethod: "raw_file"` and
`consistencyGuaranteed: false`.

The summary distinguishes the following states:

- `skipped_missing` with `no_account_state_paths` when no path exists.
- `snapshot_unavailable` with `safe_snapshot_unavailable` when sources exist
  but none can be captured safely.
- `partial` with `safe_snapshot_partial` when at least one existing source is
  archived and another existing source is unavailable.
- `backed_up` with `same_device_restore_only` when every existing source is
  captured according to policy; missing optional/legacy paths do not make it
  partial.

## Restore behavior

Restore opens the direct ZIP and validates its manifest versions, required
entries, and every manifest SHA-256 checksum before any device mutation. It
rejects unsafe or duplicate ZIP paths. Archive tar files
are inspected locally with `System.Formats.Tar`; absolute paths, traversal,
links, device nodes, FIFOs, and entries outside the expected component subtree
are rejected.

Restore options mirror all seven backup components. Properties and managed
settings use their recorded status values: `backed_up` restores the exact
value, while `skipped_missing` and `skipped_empty` clear only the known managed
target. Excluded and unknown properties/settings are never replayed. App
payloads require an installed target package, verifiable signing certificate,
and a target version that is not older than the source; the target UID is
queried on the target device and is never copied from the archive. CE aliases
are extracted once to the canonical target path. A newer target version is
allowed and is reported as a compatibility warning.

Keybox, SSAID, Google App Data, and Google Account State remain explicit
optional components. A Keybox selection is also meaningful when the archive
contains only the captured `keybox.enabled` property: a missing keybox file
with a saved disabled or empty value restores that property without inventing
a file; a saved enabled value requires the keybox payload and is blocked when
it is missing. Keybox and SSAID payloads are validated and installed under
root with target metadata and `restorecon`. SSAID replacement also stops
the SettingsProvider process and is followed by the final reboot so its
in-memory state is not retained. Account SQLite snapshots are staged and
checked with `PRAGMA quick_check` when `sqlite3` is available; account files
are replaced only after the target framework is confirmed alive, the supported
`stop zygote` service flow has stopped it, and `system_server` is absent.
Rollback is attempted while it is stopped; Restore never issues
`ctl.start system_server`, and final recovery uses a reboot. Partial account
archives restore only the payloads that are present
and are reported as partial.

The opened archive, extracted tar files, and remote staging directory are
closed or removed on success, failure, and cancellation with bounded cleanup
tokens.
Restore reports per-component and per-package outcomes (`Succeeded`,
`Partial`, or `Failed`) and does not report success when a selected component
or package was skipped because compatibility or validation could not be
established. The dialog performs a target compatibility preflight after
inspection and before the device action is reserved; a batch keeps valid
archive inspections while reporting invalid archives separately.
