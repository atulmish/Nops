# TEST-ONLY Keys - NOT FOR PRODUCTION

## Age Test Key

**Public key:** `age1q4rff6x85y5jnatyphqst5h83njnnv9whguftdutr7t5x0g5pcgs6z9edz`

**Secret key:** See `AGE-TEST-KEY.txt` in this directory.

These keys are used ONLY for generating and decrypting test fixtures. Never use in production.

## Fixture Files

Each encrypted fixture `X.sops.<ext>` has an expected plaintext `X.sops.<ext>.plain`.

| File | Format | Description |
|------|--------|-------------|
| `secrets.sops.yaml` | YAML | Basic encrypted values, arrays, nested objects |
| `secrets.sops.json` | JSON | Basic encrypted values in JSON |
| `nested.sops.yaml` | YAML | Deep nesting + arrays (AAD path coverage) |
| `dotenv.sops.env` | Dotenv | sops_* flat prefix format |
| `ini.sops.ini` | INI | Section-oriented format |
| `binary.sops.json` | JSON | Binary-as-JSON `{data: ENC[...]}` |
| `unencrypted-types.sops.yaml` | YAML | Scalar typing for `_unencrypted` values (bool/int/float/null/date) |
| `multi-keygroup.sops.yaml` | YAML | Real 2-of-3 Shamir `key_groups` split |
| `mac-only-encrypted.sops.yaml` | YAML | `mac_only_encrypted: true` vector |
| `comments.sops.yaml` | YAML | Comment encryption (`encrypted_comment_regex`) |
| `anchors.sops.yaml` | YAML | Anchor/alias over unencrypted values (alias hand-inserted after `sops -e`; `sops -d` verifies it) |
| `ini-default-section.sops.ini` | INI | Keys before any section (DEFAULT), backslashes in values, section head comment |
| `numbers.sops.json` | JSON | Unencrypted integers beyond 2^53 and beyond the int64 range |
| `pgp.sops.yaml` | YAML | PGP recipient whose data key is encrypted to the `[E]` subkey (`PGP-TEST-KEY.asc`) |
| `special-floats.sops.yaml` | YAML | Unencrypted `.nan` / `.inf` / `-.inf` |
| `footcomments.sops.yaml` | YAML | Foot comments at block ends, comments inside sequences (`encrypted_comment_regex`) |

## Usage

To decrypt any fixture:
```bash
export SOPS_AGE_KEY_FILE=AGE-TEST-KEY.txt
sops --decrypt secrets.sops.yaml
```

`AGE-TEST-KEY.txt` holds three TEST-ONLY identities. The single-recipient
fixtures use Key A; `multi-keygroup.sops.yaml` requires any two of Keys A/B/C.

`PGP-TEST-KEY.asc` is a TEST-ONLY OpenPGP key (no passphrase) with a separate
encryption subkey; its primary fingerprint is in the file header.
