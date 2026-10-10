# TEST-ONLY Keys — NOT FOR PRODUCTION

## Age Test Key

**Public key:** `age1q4rff6x85y5jnatyphqst5h83njnnv9whguftdutr7t5x0g5pcgs6z9edz`

**Secret key:** See `AGE-TEST-KEY.txt` in this directory.

These keys are used ONLY for generating and decrypting test fixtures. Never use in production.

## Fixture Files

| File | Format | Description |
|------|--------|-------------|
| `secrets.sops.yaml` | YAML | Basic encrypted values, arrays, nested objects |
| `nested.sops.yaml` | YAML | Deep nesting + arrays (AAD path coverage) |
| `dotenv.sops.env` | Dotenv | sops_* flat prefix format |
| `ini.sops.ini` | INI | Section-oriented format |
| `binary.sops.json` | JSON | Binary-as-JSON `{data: ENC[...]}` |
| `mac-only-encrypted.sops.yaml` | YAML | `mac_only_encrypted: true` vector |
| `comments.sops.yaml` | YAML | Comment encryption (`encrypted_comment_regex`) |

## Usage

To decrypt any fixture:
```bash
export SOPS_AGE_KEY=$(grep 'AGE-SECRET-KEY' AGE-TEST-KEY.txt)
sops --decrypt secrets.sops.yaml
```
