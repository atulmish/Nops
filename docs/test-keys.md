# Test Keys — Nops

Guidelines for managing test-only cryptographic material used in fixtures and CI.

## Location

Test keys are stored in:

```
tests/Nops.Core.Tests/Fixtures/Keys/
├── age-test-identity.txt      # Age private key (Bech32)
├── pgp-test-key.asc            # PGP private key (ASCII-armored)
└── TEST-ONLY-README.md        # This file
```

## Security Rules

1. **NEVER commit production keys** — only test/dev keys
2. **NEVER reuse test keys in production** — they are public in the repo
3. **Rotate test keys periodically** — invalidate old fixtures after rotation
4. **Document key purpose** — each key file should state what it's used for

## Age Test Identity

```
# tests/Nops.Core.Tests/Fixtures/Keys/age-test-identity.txt
AGE-SECRET-KEY-1234567890abcdef...  # TEST ONLY — NOT FOR PRODUCTION
```

Used to:
- Generate SOPS-encrypted test fixtures (`sops -e`)
- Decrypt test fixtures in CI (`sops decrypt`)
- Verify Nops decryption matches `sops decrypt` output

### Generating a New Age Test Key

```bash
age-keygen -o tests/Nops.Core.Tests/Fixtures/Keys/age-test-identity.txt
# Copy the public key to fixture YAML files as recipient
```

## PGP Test Key

```
# tests/Nops.Core.Tests/Fixtures/Keys/pgp-test-key.asc
-----BEGIN PGP PRIVATE KEY BLOCK-----
...
-----END PGP PRIVATE KEY BLOCK-----
```

Used for:
- JSON format test fixtures (PGP-encrypted)
- Testing PgpMasterKey decryption path

### Generating a New PGP Test Key

```bash
gpg --full-generate-key  # Choose RSA 4096, no expiry
gpg --armor --export-secret-keys test@example.com > pgp-test-key.asc
gpg --delete-secret-and-public-key test@example.com  # Remove from keyring
```

## Fixture Files

Encrypted fixtures use these keys:

| Fixture | Key Type | Purpose |
|---|---|---|
| `secrets.sops.yaml` | Age | Basic YAML decryption |
| `secrets.sops.json` | PGP | JSON decryption |
| `nested.sops.yaml` | Age | Nested structures + arrays |
| `dotenv.sops.env` | Age | Dotenv format |
| `ini.sops.ini` | Age | INI format |
| `binary.sops.json` | Age | Binary-as-JSON format |
| `multi-keygroup.sops.yaml` | Age (2-of-3) | Shamir secret sharing |
| `mac-only-encrypted.sops.yaml` | Age | MAC-only-encrypted vector |
| `comments.sops.yaml` | Age | Comment handling (sops#2243) |

## CI Verification

CI verifies fixtures match plaintext:

```yaml
- name: Verify SOPS fixtures
  run: |
    for f in tests/Nops.Core.Tests/Fixtures/*.sops.*; do
      plain="${f%.sops.*}.plain"
      sops decrypt "$f" > "/tmp/decrypted"
      diff "$plain" "/tmp/decrypted" || { echo "Mismatch: $f"; exit 1; }
    done
```

## Key Rotation Procedure

1. Generate new test keys (see above)
2. Regenerate all fixtures with new keys:
   ```bash
   sops -e --age "$(cat age-test-identity.txt | age-keygen -p)" secrets.yaml > secrets.sops.yaml
   ```
3. Update `.plain` expected files
4. Commit all changes together
5. Verify CI passes

## Never Do These

- [ ] Use test keys in any non-test environment
- [ ] Share test keys outside the organization
- [ ] Store test keys in version control without clear TEST ONLY labeling
- [ ] Reuse the same test key across multiple repos (compromises isolation)
