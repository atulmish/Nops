# Test Keys - Nops

Guidelines for managing test-only cryptographic material used in fixtures and CI.

## Location

Test keys are stored in:

```
tests/Nops.Core.Tests/Fixtures/
├── AGE-TEST-KEY.txt           # Age private identities (Bech32), TEST ONLY
└── README.md                  # Notes about the test-only keys
```

> PGP fixtures use a separate test key that is not committed; those tests are
> skipped locally unless the key is supplied. Age fixtures use `AGE-TEST-KEY.txt`.

## Security Rules

1. **NEVER commit production keys** - only test/dev keys
2. **NEVER reuse test keys in production** - they are public in the repo
3. **Rotate test keys periodically** - invalidate old fixtures after rotation
4. **Document key purpose** - each key file should state what it's used for

## Age Test Identity

`AGE-TEST-KEY.txt` contains three TEST-ONLY age identities:

```
# tests/Nops.Core.Tests/Fixtures/AGE-TEST-KEY.txt
# Key A - primary fixtures
AGE-SECRET-KEY-...  # TEST ONLY - NOT FOR PRODUCTION
# Key B - 2-of-3 Shamir fixture
AGE-SECRET-KEY-...
# Key C - 2-of-3 Shamir fixture
AGE-SECRET-KEY-...
```

Key A encrypts the single-recipient fixtures. Keys A, B and C each encrypt one
key group of `multi-keygroup.sops.yaml`, which uses a Shamir threshold of 2.

Used to:
- Generate SOPS-encrypted test fixtures (`sops -e`)
- Decrypt test fixtures in CI (`sops decrypt`)
- Verify Nops decryption matches `sops decrypt` output

### Generating a New Age Test Key

```bash
age-keygen -o key-A.txt
# Copy the public key to fixture YAML files as recipient
```

## PGP Test Key

No PGP test key is committed to the repository. PGP fixtures, if added, must be
generated with a locally generated test key and are not decrypted by the
committed CI setup.

Used for:
- JSON format test fixtures (PGP-encrypted) - not currently committed
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
| `secrets.sops.json` | Age | Basic JSON decryption |
| `nested.sops.yaml` | Age | Nested structures + arrays |
| `dotenv.sops.env` | Age | Dotenv format |
| `ini.sops.ini` | Age | INI format |
| `binary.sops.json` | Age | Binary-as-JSON format |
| `unencrypted-types.sops.yaml` | Age | Scalar typing with `_unencrypted` values |
| `multi-keygroup.sops.yaml` | Age (2-of-3) | Shamir secret sharing |
| `mac-only-encrypted.sops.yaml` | Age | MAC-only-encrypted vector |
| `comments.sops.yaml` | Age | Comment handling (sops#2243) |

## CI Verification

CI verifies fixtures match plaintext:

```yaml
- name: Verify fixtures match plaintext
  env:
    SOPS_TEST_AGE_KEY: ${{ secrets.SOPS_TEST_AGE_KEY }}
  run: |
    # The multi-keygroup fixture needs two of the three committed identities.
    committed=$(grep AGE-SECRET-KEY tests/Nops.Core.Tests/Fixtures/AGE-TEST-KEY.txt | tr '\n' ' ')
    export SOPS_AGE_KEY="${SOPS_TEST_AGE_KEY:-} $committed"
    for f in tests/Nops.Core.Tests/Fixtures/*.sops.*; do
      plain="${f}.plain"
      [ -f "$plain" ] || continue
      sops decrypt "$f" | diff -q "$plain" - || { echo "Mismatch: $f"; exit 1; }
    done
```

## Key Rotation Procedure

1. Generate new test keys (see above)
2. Regenerate all fixtures with new keys:
   ```bash
   sops -e --age "$(age-keygen -y < key-A.txt)" secrets.yaml > secrets.sops.yaml
   ```
3. Update `.plain` expected files
4. Commit all changes together
5. Verify CI passes

## Never Do These

- [ ] Use test keys in any non-test environment
- [ ] Share test keys outside the organization
- [ ] Store test keys in version control without clear TEST ONLY labeling
- [ ] Reuse the same test key across multiple repos (compromises isolation)
