# Test Keys - Nops

Guidelines for managing test-only cryptographic material used in fixtures and CI.

## Location

Test keys are stored in:

```text
tests/Nops.Core.Tests/Fixtures/
├── AGE-TEST-KEY.txt           # Age private identities (Bech32), TEST ONLY
├── PGP-TEST-KEY.asc           # OpenPGP secret key (no passphrase), TEST ONLY
└── README.md                  # Notes about the test-only keys
```

## Security Rules

1. **NEVER commit production keys** - only test/dev keys
2. **NEVER reuse test keys in production** - they are public in the repo
3. **Rotate test keys periodically** - invalidate old fixtures after rotation
4. **Document key purpose** - each key file should state what it's used for

## Age Test Identity

`AGE-TEST-KEY.txt` contains three TEST-ONLY age identities:

```text
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

`PGP-TEST-KEY.asc` is a TEST-ONLY OpenPGP key with no passphrase: an ed25519
`[SC]` primary key plus a cv25519 `[E]` encryption subkey, so tests cover gpg
encrypting the data key to the subkey rather than the primary key.

- Primary fingerprint: `86498AF4273A49F7D96FF1A62C9F3DFF82C80F58`
- Used by `pgp.sops.yaml` and the `PgpMasterKey` decryption tests
- CI imports it with `gpg --batch --import` before verifying fixtures

### Generating a New PGP Test Key

Use a throwaway keyring so the key never touches your own:

```bash
export GNUPGHOME="$(mktemp -d)"
gpg --batch --pinentry-mode loopback --passphrase '' \
  --quick-gen-key "Nops Test (TEST-ONLY) <test@nops.invalid>" ed25519 sign never
FP=$(gpg --list-keys --with-colons | awk -F: '/^fpr/{print $10; exit}')
gpg --batch --pinentry-mode loopback --passphrase '' --quick-add-key "$FP" cv25519 encr never
gpg --armor --export-secret-keys "$FP" > PGP-TEST-KEY.asc
sops -e --pgp "$FP" pgp.yaml > pgp.sops.yaml
```

Update `PgpTestFingerprint` in `SopsFixtureTests.cs` after rotating.

### Passphrase-protected PGP key

`PGP-PASSPHRASE-KEY.asc` (armored) and `PGP-PASSPHRASE-KEY.gpg` (binary) hold a second TEST-ONLY RSA key, protected
with the passphrase `nops-test-passphrase`. They cover the passphrase paths of `PgpMasterKey` and loading a
binary (non-armored) key ring.

- Primary fingerprint: `5B3E2BB86CDF737A3B585240C0914CD66DE561F6`
- Used by `pgp-passphrase.enc.yaml`, written by real `sops`. It is deliberately not named `*.sops.*`, because the
  CI fixture check decrypts those with gpg, which cannot prompt for a passphrase.

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
| `anchors.sops.yaml` | Age | YAML anchors/aliases over unencrypted values |
| `footcomments.sops.yaml` | Age | Block-end comments, comments inside sequences |
| `ini-default-section.sops.ini` | Age | INI keys before any section, backslashes in values |
| `numbers.sops.json` | Age | JSON integers beyond 2^53 / the int64 range |
| `special-floats.sops.yaml` | Age | YAML `.nan` / `.inf` |
| `pgp.sops.yaml` | PGP | Data key encrypted to the PGP encryption subkey |
| `typed-values.sops.yaml` | Age | Nested sequences, nulls, empty lists, time, float and bool values |
| `pgp-passphrase.enc.yaml` | PGP (passphrase) | Passphrase-protected key, see above (not checked by CI's gpg step) |
| `age-cctv/*` | Age | 101 vectors from the C2SP age test suite, run by `AgeCctvTests` |

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
