# Device backup format

The backup writer stores an encrypted, versioned envelope around the existing
ZIP payload. Restore is not implemented; this document defines the bytes a
future restore workflow must read.

## Outer `.ddcbak` envelope, version 1

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

Encryption and HMAC are streaming operations, so the complete backup is not
loaded into memory. HMAC is checked with a constant-time comparison before the
partial destination is renamed to its final `.ddcbak` name.

## Internal payload and lifecycle

The encrypted payload is the existing ZIP structure containing entries such as
`manifest.json`, `properties.json`, `settings.json`, app payloads, optional
payloads, and internal SHA-256 checksums. The internal manifest uses
`FormatVersion = 2`; `EncryptionFormatVersion = 1` identifies the outer
envelope. These are separate version numbers.

The writer performs this sequence:

1. Create the ZIP under `Path.GetTempPath()/DeepDroidChanger/Backup/<session>`.
2. Close and validate the ZIP and its internal checksums.
3. Stream-encrypt it into `<name>.ddcbak.partial`.
4. Validate the envelope length and HMAC.
5. Delete the plaintext temporary directory.
6. Atomically rename the verified partial file to `<name>.ddcbak`.

Failure and cancellation attempt to remove both the partial encrypted file and
the plaintext temporary directory. The writer never falls back to a plaintext
`.ddcbak`.

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
