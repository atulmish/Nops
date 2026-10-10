# Nops - In-Process SOPS Decryptor for .NET

## Goal

A pure managed .NET library that decrypts SOPS-encrypted files (YAML, JSON, Dotenv, INI, Binary) in-process and exposes them as ordered trees, `IDictionary<string, string>` (flattened), or typed objects. Optional `Microsoft.Extensions.Configuration` provider for ASP.NET Core / generic host integration.

**Not a CLI tool. Not a subprocess wrapper.** True in-process decryption with zero native dependencies.

> Reference: upstream Go SOPS was kept at `sops/` during the port as a format/crypto reference only; it has since been removed from the tree. SOPS is MPL-2.0; Nops reimplements the format (no code copy).

---

## Why This Exists

| Existing Tool | Approach | Problem |
|---|---|---|
| `sops` (CLI) | Native Go binary | Requires SOPS installed, not embeddable |
| `dotnet-sops` (26⭐) | Downloads native SOPS binary, calls it as subprocess | Native dependency on disk, version pinning, process spawn overhead |
| No .NET library exists | - | Gap: no in-process SOPS decryption for .NET |

**Nops fills the gap:** pure managed code, zero native deps, embeddable in any .NET app.

---

## Architecture

```
Nops/
├── src/
│   ├── Nops.Core/                          # Core NuGet (~200KB, net8.0+)
│   │   ├── Crypto/
│   │   │   ├── AesGcmDecryptor.cs          # AES-256-GCM, 32-byte nonce (see Decision 2)
│   │   │   ├── DataKeyResolver.cs           # Try master keys until one succeeds; Shamir combine
│   │   │   └── ToBytes.cs                   # Canonical MAC input encoding (port of sops.ToBytes)
│   │   ├── Keys/
│   │   │   ├── IMasterKeyProvider.cs        # Interface for all key types
│   │   │   ├── AgeMasterKey.cs              # Wraps AgeSharp for age decryption
│   │   │   └── PgpMasterKey.cs              # BouncyCastle OpenPGP decryption
│   │   │   ├── AwsKmsMasterKey.cs           # AWSSDK.KMS Decrypt (optional ref, separate package)
│   │   │   ├── GcpKmsMasterKey.cs           # Google.Cloud.KMS.V1 (optional ref, separate package)
│   │   │   ├── AzkvMasterKey.cs             # Azure.Security.KeyVault.Keys (optional ref, separate package)
│   │   │   └── VaultMasterKey.cs            # HashiCorp.Vault.API transit (optional ref, separate package)
│   │   ├── Stores/
│   │   │   ├── SopsMetadata.cs              # .sops dict deserialization (flat lists → runtime KeyGroups)
│   │   │   ├── EncryptedValueParser.cs      # Parse ENC[AES256_GCM,...] (no trailing $ anchor)
│   │   │   ├── TreeModel.cs                 # Ordered TreeBranch/TreeItem (order-preserving, not Dictionary)
│   │   │   ├── TreeWalker.cs                # Port of walkBranch: path + commentsStack + shouldBeEncrypted
│   │   │   ├── YamlStore.cs                 # YamlDotNet - YAML load/emit (order + comments preserved)
│   │   │   ├── JsonStore.cs                 # System.Text.Json - JSON load/emit (ordered model)
│   │   │   ├── DotenvStore.cs               # Full-flatten (sops_* prefix) + \n unescape
│   │   │   ├── IniStore.cs                  # Section-oriented (ini.v1 semantics port)
│   │   │   └── BinaryStore.cs               # Binary-as-JSON {data: ENC[...]} (explicit scope; see Phase 1)
│   │   └── SopsFile.cs                      # Main entry point: Load + Decrypt → tree / key-values / T
│   │
│   └── Nops.Configuration/                 # ASP.NET Core integration (optional NuGet)
│       ├── SopsConfigurationProvider.cs     # IConfigurationProvider impl
│       ├── SopsConfigurationSource.cs       # IConfigurationSource impl
│       └── SopsOptions.cs                   # Fluent options builder (options bag, not positional args)
│
├── samples/
│   ├── MinimalApiSample/                    # Minimal API loading sops.yaml at startup
│   └── ConfigurationSample/                 # AddSops() in Program.cs
│
├── tests/
│   ├── Nops.Core.Tests/
│   │   ├── Crypto/
│   │   ├── Stores/
│   │   └── Fixtures/                        # Real SOPS-encrypted test files (YAML+JSON) + TEST-ONLY keys
│   └── Nops.Configuration.Tests/
│
├── benchmarks/
│   └── Nops.Benchmarks/                     # BenchmarkDotNet: decrypt time, memory
│
└── Nops.sln
```

> `sops/` (Go upstream) lives at repo root as reference. Do not nest `Nops.sln` under `Nops/`. Keep `third_party/` or document `sops/` as reference-only so pack/publish globs don't pick it up.

---

## Build & Toolchain Lock (do not guess)

- SDK: .NET 8 SDK (`global.json` pins exact version; agent must run `dotnet --info` and paste output in first PR).
- TFMs: `net8.0` only for MVP. No `netstandard2.0` / `net48` backports.
- C#: latest stable, `Nullable: enable`, `ImplicitUsings: enable`, `TreatWarningsAsErrors: true`.
- Package versions: agent must resolve **exact** versions from NuGet (`dotnet package search`) and pin them in `.csproj`. Do NOT invent API names from memory. If a type/method in this plan doesn't exist in the resolved version, stop and report - don't shim with a guessed name.

## External Dependencies (Core)

| Dependency | Purpose | License | Hallucination guard |
|---|---|---|---|
| `AgeSharp` | age X25519 + scrypt + ChaCha20-Poly1305 stanza decrypt | MIT | No `AgeKeygen`/`Decrypt()` guesses. Phase 0 must list real types via `dotnet` + paste a compiling snippet. `enc` is an age stanza block, not PGP armor. |
| `BouncyCastle.Cryptography` | PGP/OpenPGP decrypt; **also AES-GCM backend candidate** (`GcmBlockCipher`) | MIT | Pin exact version (e.g. 2.x). PGP `enc` is ASCII-armored `-----BEGIN PGP MESSAGE-----`. Passphrase handling must be explicit: support or throw `NotSupportedException` with message - never silently ignore. |

## External Dependencies (Cloud KMS - optional refs, separate packages)

| Dependency | Purpose |
|---|---|
| `AWSSDK.KMS` | AWS KMS Decrypt API |
| `Google.Cloud.KMS.V1` | Google Cloud KMS decrypt |
| `Azure.Security.KeyVault.Keys` | Azure Key Vault unwrap |
| `HashiCorp.Vault.API` | HashiCorp Vault transit secrets engine |

`Nops.Core` has zero cloud refs. Each provider ships as `Nops.Core.AwsKms`, `Nops.Core.GcpKms`, etc.

## External Dependencies (Configuration)

| Dependency | Purpose |
|---|---|
| `Microsoft.Extensions.Configuration` | IConfigurationProvider/Source contract |
| `YamlDotNet` | YAML parsing |

---

## SOPS Format Reference

### Encrypted Value Format

Upstream regex (`sops/aes/cipher.go:55`):

```
^ENC\[AES256_GCM,data:(.+),iv:(.+),tag:(.+),type:(.+)\]
```

No trailing `$` anchor - match upstream behavior exactly.

- `data`: AES-GCM ciphertext **minus last 16 bytes** (base64, `StdEncoding`)
- `iv`: **32-byte** nonce (base64). Not 12-byte.
- `tag`: last 16 bytes of GCM output (`AES BlockSize`, base64)
- `type`: one of `str | int | float | bytes | bool | time | comment`
  - `bool`: `"True"` / `"False"` (Python-cased, `ToBytes` + `cipher.go:179-183`)
  - `float`: `FormatFloat('f', -1, 64)` (no trailing-zero padding)
  - `time`: `MarshalText` (RFC3339-ish)
  - `comment`: encrypted YAML/Dotenv comment (`#ENC[...]`, value is `Comment{Value}`)
  - Empty string / empty bytes / empty comment → stored as `""`, never as `ENC[...]` (`isEmpty`)

Data key: 32 bytes (AES-256). GCM tag: 16 bytes.

### MAC (correction: SHA-512 hex, stored encrypted)

- **Not HMAC-SHA256, not base64.** Upstream (`sops/sops.go:535,600,658`):
  `hash := sha512.New()` → `fmt.Sprintf("%X", hash.Sum(nil))` (uppercase hex).
- MAC input per value is canonical `ToBytes(v)` (`sops.go:980-1003`):
  `string→raw`, `int→Itoa`, `float64→FormatFloat('f',-1,64)`, `bool→True/False`, `[]byte→raw`, `time→MarshalText`, `Comment→recurse on Value`. Unknown types are errors.
- Hash covers values **in tree-walk order** (ordered `TreeBranch`, not a `Dictionary`). Order matters.
- The `mac` field on disk is itself **encrypted** with the data key:
  `mac: ENC[AES256_GCM,...,type:str]` (see `sops/example.yaml:35`).
  Decrypt it with the data key first, then compare against recomputed `%X` hash.
- `mac_only_encrypted` (bool): if true, hash is seeded with fixed prefix
  `8a3fd2ad54ce66527b1034f3d147beb0bb975b3bf44f72c6fdadec8176f27d69`
  (`echo -n sops | sha256sum`, `sops.go:93,536-539,601-604`) and only covers values where `shouldBeEncrypted == true`.
- Comments inside sequences are excluded from MAC on both encrypt and decrypt paths (regression test `mac_comment_test.go` for getsops/sops#2243). Port that test.

#### `ToBytes` truth table (exact - copy into unit tests, do not re-derive)

| Input (C#) | `ToBytes` output (UTF-8) |
|---|---|
| `"hello"` | `hello` |
| `42` (int) | `42` (`Itoa`) |
| `3.14` (double) | `3.14` (`FormatFloat('f',-1,64)` - no trailing zeros) |
| `true` / `false` | `True` / `False` (capitalized) |
| `byte[]{0x01,0x02}` | raw bytes |
| `DateTime` | `MarshalText` (RFC3339, e.g. `2024-01-01T00:00:00Z`) |
| `Comment{Value:"x"}` | recurse on `"x"` |
| `""` / empty bytes / empty comment | skipped upstream (`isEmpty`) - never hashed as `ENC[...]` |

#### AAD examples (exact - copy into walker tests)

| YAML path | `path` slice | AAD string |
|---|---|---|
| `myapp1: ENC[...]` | `["myapp1"]` | `myapp1:` |
| `app2.db.user: ENC[...]` | `["app2","db","user"]` | `app2:db:user:` |
| `an_array[0..3]` (any element) | `["an_array"]` | `an_array:` (same for all elements - no index) |
| `this.is.a.nested.value` | `["this","is","a","nested","value"]` | `this:is:a:nested:value:` |

### Metadata field table (exact YAML keys - no renaming)

`lastmodified | mac | pgp | age | kms | gcp_kms | azure_kv | hckms | hc_vault | shamir_threshold | unencrypted_suffix | encrypted_suffix | unencrypted_regex | encrypted_regex | unencrypted_comment_regex | encrypted_comment_regex | mac_only_encrypted | version`. Unknown keys: ignore (forward-compat). Missing `shamir_threshold`/`0`: threshold = number of groups. Duplicate `sops` key in branch 0: error. `sops` key outside branch 0: error.

### Forbidden (agent must not do these)

- No custom AES-GCM / ChaCha20 / SHA-512 implementations. Use BCL + BouncyCastle only.
- No `HMACSHA256` for MAC. It's plain `SHA512` → uppercase hex.
- No `Dictionary<string,object>` as the tree model. Ordered `List<TreeItem>` only until after MAC verify.
- No `string.Split(':')` on ENC bodies, no `Trim()` on base64, no `Jwt`-style base64url. `StdEncoding` only.
- No `float.ToString()` for `ToBytes` - must match Go `FormatFloat('f',-1,64)` (test `0.1`, `1e20`).
- No `bool.ToString().ToLower()` - must be `True`/`False`.
- No `DateTime.ToString("o")` for `ToBytes` - must match Go `MarshalText`.
- No gRPC `keyservice`, no subprocess `sops` fallback, no GPG-agent auto-discovery.
- No inventing `AgeSharp`/`BouncyCastle` method names. Discover-then-pin (see Build lock).

### Metadata Location

`.sops` top-level key in YAML/JSON. On disk it holds **flat lists** (no `key_groups` wrapper):

```yaml
sops:
  lastmodified: "2025-08-06T18:55:05Z"
  mac: ENC[AES256_GCM,...]   # encrypted, decrypt with data key before compare
  pgp: [{fp, enc, created_at}, ...]
  age: [{recipient, enc}, ...]
  kms: [...]
  gcp_kms: [...]
  azure_kv: [...]
  hckms: [...]
  hc_vault: [...]
  shamir_threshold: 2        # optional; 0/unset → len(KeyGroups)
  unencrypted_suffix: _unencrypted
  encrypted_suffix: null
  unencrypted_regex: null
  encrypted_regex: null
  unencrypted_comment_regex: null
  encrypted_comment_regex: null
  mac_only_encrypted: false
  version: 3.10.2
```

- `KeyGroups` + `ShamirThreshold` are **runtime** constructs (`sops.go:682-699`). File → runtime mapping lives in `stores/stores.go` (via `mapstructure,WeaklyTypedInput`). Expect polymorphic entries, timestamps, missing keys.
- `DefaultDecryptionOrder = [age, pgp]` (`sops.go:66`) is CLI convenience, not file semantics. Resolver tries all keys; file order is not a priority contract.

### Key Groups (Shamir Secret Sharing)

- Single group → whole data key encrypted under each master key in the group; any one success recovers it.
- Multiple groups → `shamir.Split(dataKey, nGroups, threshold)` at encrypt time; decrypt collects ≥ threshold shares then `shamir.Combine`.
- `shamir_threshold == 0` means `len(KeyGroups)` (all groups required).
- Resolver behavior: iterate groups → try each master key → collect shares / short-circuit single-group. Surface per-key errors; fail only if threshold not met.

### Tree Walk, AdditionalData, shouldBeEncrypted

Port `walkBranch` + `walkSlice` + `walkValue` + `shouldBeEncrypted` (`sops.go:330-517`), don't simplify:

- `additionalData = strings.Join(path, ":") + ":"`.
- **Arrays do NOT add an index to `path`.** `walkSlice` (`sops.go:368-397`) passes `path` unchanged for every element (`walkValue(v, path, ...)`). All items in one list share the parent's AAD. Do NOT invent `parent:0:child` AAD - that pattern is only for the .NET `IConfiguration` flattening in Phase 3, never for SOPS AAD/MAC.
- `walkValue` types: `string | []byte→string | int | bool | float64 | time.Time | Comment | TreeBranch→recurse walkBranch | []interface{}→recurse walkSlice | nil→skip`. Anything else is an error.
- `nil` values are skipped (never encrypted, never hashed).
- `shouldBeEncrypted(path, commentsStack, isComment)`:
  - `unencrypted_suffix`: any path element with suffix → skip.
  - `encrypted_suffix`: default skip unless any element matches.
  - `unencrypted_regex` / `encrypted_regex`: matched against each path element (Go `regexp.Match`, per-element).
  - `unencrypted_comment_regex` / `encrypted_comment_regex`: matched against `commentsStack` (active `#` comments). `encrypted_comment_regex` skips the comment line itself that triggered the match.
- Comment keys (`item.Key is Comment`) and comment values must be walked - they can themselves be `ENC[...,type:comment]`.

---

## Decryption Flow

```
SOPS file (YAML/JSON/Dotenv/INI/Binary)
  │
  ├─→ parse format into ORDERED tree (TreeBranch/TreeItem; preserve order + comments)
  │     Dotenv: strip sops_* flat prefix (MetadataFlattenFull), unescape \n
  │     INI: section → TreeBranch; Binary: JSON {data: ENC[...]}
  │
  ├─→ extract .sops metadata from first branch (error on duplicate/missing)
  │     map flat lists → runtime KeyGroups + ShamirThreshold
  │
  ├─→ resolve data key (32 bytes):
  │     for each key group:
  │       for each master key in group:
  │         try Decrypt(enc_data_key) → success? collect share / return if single-group
  │     if Shamir (nGroups > 1): Combine(shares) once ≥ threshold shares
  │     surface per-key errors; fail if threshold unmet
  │
  ├─→ decrypt stored mac with data key (it is ENC[...]) → expectedMacHex
  │
  ├─→ walk tree in order, for each leaf where shouldBeEncrypted(path, commentsStack):
  │     additionalData = Join(path, ":") + ":"
  │     AesGcmDecrypt(dataKey, iv[32B], data, tag[16B], additionalData)
  │       NOTE: .NET AesGcm may reject 32B nonce → BouncyCastle GcmBlockCipher fallback
  │     cast by type: str/int/float/bytes/bool(True/False)/time(MarshalText)/comment
  │     feed ToBytes(decrypted) into SHA512 hash (skip Comment results per upstream;
  │       seed hash with MACOnlyEncryptedInitialization if flag set)
  │
  ├─→ verify MAC: computed %X == expectedMacHex else MacMismatch error
  │
  └─→ return: (a) ordered tree, (b) flattened IDictionary<string,string> (":"-joined),
      or (c) deserialized T. Flatten AFTER MAC verify, never before.
```

---

## Phase Breakdown (A–Z, executable - do in order, no skipping)

Conventions: each step lists **Files**, **API**, **Tests first**, **Gate**. TDD mandatory (see Test Strategy). Commands assume repo root.

### Phase 0 - Spike & Scaffold (0.5–1 day)

**Step A - Pin toolchain + create solution (no guessing versions).**
- Files: `global.json`, `Nops.sln`, `src/Nops.Core/Nops.Core.csproj` (`net8.0`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`), `tests/Nops.Core.Tests/Nops.Core.Tests.csproj` (xUnit).
- Commands: `dotnet --info` (paste in PR) → `dotnet new sln -n Nops` → `dotnet new classlib -o src/Nops.Core -f net8.0` → `dotnet new xunit -o tests/Nops.Core.Tests -f net8.0` → `dotnet sln add` both → `dotnet build`.
- Gate: `dotnet build` + `dotnet test` (empty) green.

**Step B - AES-GCM backend decision (BLOCKING).**
- Question: does `System.Security.Cryptography.AesGcm` accept the 32-byte IV from `sops/example.yaml:1`?
- Test first: `tests/Nops.Core.Tests/Crypto/AesGcmTests.cs::Decrypts_ExampleYaml_Myapp1` - hardcode `data/iv/tag` + known data key (generate via `sops` CLI on a test file, or use fixture from Step Q). AAD = `myapp1:`.
- Try S.S.C. backend. Expected: throws (`nonce`/`tag` size). Then try BouncyCastle `GcmBlockCipher` (`AesEngine` + `GcmBlockCipher`, 128-bit mac). Record decision in Decision 2 + `src/Nops.Core/Crypto/IAesGcmDecryptor.cs`.
- API: `byte[] Decrypt(byte[] key32, byte[] nonce32, byte[] ciphertext, byte[] tag16, byte[] aad)`.
- Gate: one backend decrypts the real vector. No padded/truncated nonces allowed.

**Step C - YamlDotNet order/comments + JSON order probe.**
- Test: load `sops/example.yaml`, assert mapping order == file order and `#ENC[...,type:comment]` nodes visible.
- Implementation note: use `YamlMappingNode.Children` (ordered) - never deserialize to `Dictionary`. For JSON use `JsonDocument` (preserve element order) mapped into `TreeBranch`, not `JsonSerializer<Dictionary>`.
- Gate: order + comment test green, or document the YamlDotNet setting that makes it green.

**Step D - AgeSharp / BouncyCastle discovery (no invented APIs).**
- Commands: add pinned `AgeSharp` + `BouncyCastle.Cryptography` refs, then list real types (`dotnet build` + IDE object browser / ` Assembly.GetTypes()` dump in a scratch test).
- Deliverable: paste compiling snippet that decrypts one age stanza / one PGP block into the PR. If the guessed class doesn't exist, stop and report.
- Gate: snippet compiles; versions pinned in `.csproj`.

### Phase 1 - Core MVP (4–5 days)

**Step E - `ToBytes` (canonical MAC input).**
- Files: `src/Nops.Core/Crypto/ToBytes.cs` (`public static byte[] ToBytes(object? value)`).
- Tests: `Crypto/ToBytesTests.cs` from the truth table (`True/False`, `FormatFloat('f',-1,64)`, RFC3339 time, `Comment` recurse, unknown type throws). Include `0.1`, `1e20`, `0.0` float edge cases.
- Gate: 100% branch coverage on this file.

**Step F - `EncryptedValueParser`.**
- Files: `src/Nops.Core/Stores/EncryptedValueParser.cs` → `record EncryptedValue(byte[] Data, byte[] Iv, byte[] Tag, string DataType)` + `static bool TryParse(string s, out EncryptedValue? v)`.
- Rules: upstream regex no `$`; `Convert.FromBase64String` (`StdEncoding`, not Url); `""` → not-encrypted; unknown `type` preserved for later error (not swallowed).
- Tests: `Crypto/ParserTests.cs` - all 7 types, bad base64, missing fields, trailing content after `]` (must still parse per no-`$` rule).
- Gate: parser tests green.

**Step G - Ordered tree model.**
- Files: `src/Nops.Core/Stores/TreeModel.cs`:
  ```csharp
  public sealed record Comment(string Value, bool Inline = false);
  public sealed record TreeItem(object Key, object? Value); // Key: string | Comment
  public sealed class TreeBranch : List<TreeItem>;
  public sealed record SopsTree(List<TreeBranch> Branches, SopsMetadata Metadata, string FilePath);
  ```
- Forbid `Dictionary<string,object>` anywhere in Core until after MAC. `nil` → `null` (skipped by walker).
- Tests: construction + order assertion.
- Gate: model compiles; no `Dictionary` in `src/Nops.Core/Stores/*.cs` (grep gate in CI).

**Step H - Walker + `shouldBeEncrypted` (+ `walkSlice` array rule).**
- Files: `src/Nops.Core/Stores/TreeWalker.cs`:
  ```csharp
  public static string AdditionalData(IReadOnlyList<string> path) => string.Join(":", path) + ":";
  public static bool ShouldBeEncrypted(SopsMetadata m, IReadOnlyList<string> path, IReadOnlyList<IReadOnlyList<string>> commentsStack, bool isComment);
  public static T Walk<T>(...) // ports walkBranch/walkSlice/walkValue incl. Comment-key/value threading
  ```
- Rules: arrays share parent AAD (no index); `nil/null` skipped; per-element regex via `Regex.IsMatch` (Go `Match` semantics - unanchored); `encrypted_comment_regex` skips the triggering line itself.
- Tests: `WalkerTests.cs` - suffix matrix, regex matrix, comment-regex matrix, AAD examples table from this plan, nested + array + comment-key walks.
- Gate: 100% branches; AAD table tests green.

**Step I - `AesGcmDecryptor` value decrypt + type cast.**
- Files: `src/Nops.Core/Crypto/AesGcmDecryptor.cs` (`object DecryptValue(string encString, byte[] dataKey, string aad)`).
- Logic: `TryParse` → `IAesGcmDecryptor.Decrypt` → UTF-8 decode → cast by `type`: `str→string`, `int→int.Parse` (Invariant), `float→double.Parse` (Invariant, round-trip), `bool→"True"→true`, `bytes→raw bytes`, `time→DateTimeOffset.Parse`, `comment→new Comment(s)`; unknown → throw `SopsException`.
- Tests: `Crypto/AesGcmTests.cs` - round-trip vectors per type with fixed key/nonce/AAD (generate via Go `sops` CLI once, commit vectors), wrong-AAD → auth failure, wrong key → auth failure.
- Gate: all type vectors green against Go-generated fixtures.

**Step J - `SopsMetadata` (flat lists → runtime groups).**
- Files: `src/Nops.Core/Stores/SopsMetadata.cs`:
  ```csharp
  public sealed record AgeEntry(string Recipient, string Enc);
  public sealed record PgpEntry(string Fp, string Enc, DateTimeOffset? CreatedAt);
  // + Kms/GcpKms/Azkv/Hckms/HcVault entries (opaque string maps for Phase 1; typed in Phase 2)
  public sealed record SopsMetadata(string? Mac, List<AgeEntry> Age, List<PgpEntry> Pgp, ..., string? UnencryptedSuffix, string? EncryptedSuffix, string? UnencryptedRegex, string? EncryptedRegex, string? UnencryptedCommentRegex, string? EncryptedCommentRegex, bool MacOnlyEncrypted, int ShamirThreshold, string? Version, DateTimeOffset? LastModified);
  public List<List<IMasterKeyProvider>> KeyGroups { get; } // built after key providers registered
  ```
- Parsing: from `YamlMappingNode`/`JsonElement` under `sops` key; case-exact keys (see field table); unknown keys ignored; `shamir_threshold` missing/0 → set later to group count; duplicate `sops` → `SopsException`; `mac` kept as raw `ENC[...]` string (decrypted later).
- Tests: `MetadataTests.cs` - `example.yaml` parse, all suffix/regex fields, missing threshold, unknown-key tolerance, duplicate/missing `sops` errors.
- Gate: metadata tests green.

**Step K - Shamir port.**
- Files: `src/Nops.Core/Crypto/Shamir.cs` (`Split` for tests + `Combine` for prod; port `sops/shamir/shamir.go` GF(2^8) exactly).
- Tests: `Crypto/ShamirTests.cs` - Go test vectors from `sops/shamir/*_test.go` (port inputs/outputs), 2-of-3 combine, insufficient shares throws.
- Gate: Go vectors pass. No third-party Shamir lib.

**Step L - `IMasterKeyProvider` + `DataKeyResolver`.**
- Files: `Keys/IMasterKeyProvider.cs` (`string KeyType {get;} Task<byte[]?> DecryptShareAsync(string enc, CancellationToken ct)` - returns share or data key), `Crypto/DataKeyResolver.cs` (`Task<byte[]> ResolveAsync(SopsMetadata m, SopsOptions o, CancellationToken ct)`).
- Logic: build runtime groups from metadata + supplied providers; single group → first success wins; multi-group → collect ≥ threshold shares → `Shamir.Combine`; aggregate per-key exceptions into `SopsKeyException`; threshold unmet → throw.
- Tests: `KeyResolverTests.cs` - single-group first-wins, multi-group 2-of-3, all-fail aggregation, threshold-0-means-all.
- Gate: resolver tests green with fake providers (no real crypto yet).

**Step M - `AgeMasterKey` + `PgpMasterKey` (real crypto).**
- Files: `Keys/AgeMasterKey.cs` (`recipient` match → AgeSharp decrypt `enc` stanza with supplied identities), `Keys/PgpMasterKey.cs` (BouncyCastle armor → session key → data-key share; passphrase or explicit `NotSupportedException`).
- Options: `SopsOptions.AgeIdentities: IReadOnlyList<string>` (Bech32 + file paths), `PgpPrivateKeys: IReadOnlyList<(byte[] Key, string? Passphrase)>`.
- Tests: decrypt real fixture `enc` values with committed TEST-ONLY keys (see Step Q); wrong key → try-next (not throw); unsupported curve → clear error type.
- Gate: real age + real PGP share recovery green.

**Step N - `YamlStore` + `JsonStore` (ordered load + `.sops` extract).**
- Files: `Stores/YamlStore.cs` (`SopsTree LoadEncrypted(byte[])`), `Stores/JsonStore.cs` (same). No decrypt here - only parse + extract.
- Rules: YAML `sops:` top-level from branch 0; comments (`Comment` keys/values incl. `#ENC`) preserved as `Comment` nodes; JSON numbers: integers → `int` (if fits) else `double` - document the rule (matches Go `float64` vs `int` handling; verify against `stores/json/store.go`).
- Tests: `Stores/YamlStoreTests.cs`, `JsonStoreTests.cs` - `example.yaml/json` load, order preserved, `.sops` extracted, `type:comment` nodes present, duplicate-`sops` error.
- Gate: store tests green.

**Step O - `DotenvStore` + `IniStore` (flatten modes).**
- Files: `Stores/DotenvStore.cs` (`MetadataFlattenFull`: `sops_*` prefix strip on load, add on emit; `\\n`↔`\n`; `#` lines → `Comment`), `Stores/IniStore.cs` (section → nested `TreeBranch`; `;`/`#` comments per `ini/store.go`).
- Tests: `DotenvStoreTests.cs`, `IniStoreTests.cs` - round-trip with `sops` CLI-generated fixtures, newline escaping, comment preservation.
- Gate: green + `EscapeNewlines` behavior documented in code comment.

**Step P - `BinaryStore` (read-only).**
- Files: `Stores/BinaryStore.cs` - input is JSON `{"data": "ENC[...]"} (+ sops metadata)` → `byte[]`.
- Tests: `BinaryStoreTests.cs` - decrypt to bytes, non-`data` keys rejected, encrypt throws `NotSupportedException`.
- Gate: green.

**Step Q - Fixtures (TEST-ONLY keys, generate once with `sops` CLI).**
- Generate with local `sops` binary: age test identity + PGP test key (no prod keys). Files: `secrets.sops.yaml` (age), `secrets.sops.json` (PGP), `nested.sops.yaml` (nesting+arrays), `dotenv.sops.env`, `ini.sops.ini`, `binary.sops.json`, `multi-keygroup.sops.yaml` (2-of-3), `mac-only-encrypted.sops.yaml`, `comments.sops.yaml` (getsops/sops#2243).
- Commit: fixtures + `Keys/TEST-ONLY-README.md` + private test keys clearly labeled. Add CI check that `sops decrypt` output == committed `.plain` expected files.
- Gate: `sops decrypt <each>` succeeds locally; expected plaintexts committed.

**Step R - `MacVerifier` + `SopsFile` (wire-up).**
- Files: `Crypto/MacVerifier.cs` (`string ComputeMac(SopsTree decryptedTree)` - SHA-512 over `ToBytes` in walk order, seed if `MacOnlyEncrypted`, skip `Comment` results), `SopsFile.cs`:
  ```csharp
  public static SopsTree DecryptTree(byte[] fileBytes, string fileKind, SopsOptions options, CancellationToken ct = default);
  public static IReadOnlyDictionary<string,string> DecryptToDictionary(...); // flatten ":" AFTER verify
  public static T Decrypt<T>(...); // System.Text.Json bind AFTER verify
  public sealed record SopsOptions { string? FilePath; IReadOnlyList<string> AgeIdentities; IReadOnlyList<PgpKey> PgpKeys; ... KMS clients (Phase 2); };
  public sealed class SopsException : Exception; public sealed class MacMismatchException : SopsException; public sealed class SopsKeyException : SopsException { IReadOnlyList<Exception> Inner; }
  ```
- Flow: detect store → load → resolve data key → decrypt `mac` field → walk+decrypt values → compute+compare `%X` → throw `MacMismatchException` on mismatch → return.
- Tests: `MacTests.cs` (valid, tampered → mismatch, `mac_only_encrypted`, comment-in-sequence), `SopsFileTests.cs` (YAML+JSON+Dotenv+INI+Binary end-to-end vs `.plain` expected).
- Gate: Phase 1 deliverables done: `Nops.Core` packs (`dotnet pack`), full `dotnet test` green.

### Phase 2 - Cloud KMS, one provider per PR (4–6 days total)

Scaffold per provider: `src/Nops.Core.<Provider>/Nops.Core.<Provider>.csproj` (refs cloud SDK only) + `Keys/<Provider>MasterKey.cs : IMasterKeyProvider` + `tests/Nops.Core.<Provider>.Tests/`.

**Step S - AWS KMS.** `Decrypt(blob CiphertextBlob, EncryptionContext?)` via injected `IAmazonKMS` or default chain. Tests: LocalStack KMS container decrypt test (not mocks-only). Doc: IAM + `EncryptionContext` mapping.
**Step T - GCP KMS.** `KeyManagementServiceClient.Decrypt(keyName, ciphertext)` via ADC. Tests: emulator or recorded replay + live opt-in (`GCP_LIVE_TEST=1`).
**Step U - Azure Key Vault.** `KeyClient.UnwrapKey` / `CryptographyClient.Unwrap` via `DefaultAzureCredential`. Tests: Azurite/recorded + live opt-in.
**Step V - HashiCorp Vault Transit + HCKMS (separate types, don't conflate).** `Transit Decrypt` (Vault) vs HCKMS key ID format. Caller supplies address/token/mount. Tests: `vault dev` Docker decrypt test.
- Per-step gate: real (or containerized) decrypt green + README with auth setup. `Nops.Core` still has zero cloud refs (CI asserts via `dotnet list package`).

### Phase 3 - Configuration Provider (2 days)

**Step W - `SopsOptions` + `SopsConfigurationSource` + `SopsConfigurationProvider`.**
- Files: `src/Nops.Configuration/{SopsOptions.cs, SopsConfigurationSource.cs, SopsConfigurationProvider.cs, ServiceCollectionExtensions.cs}`.
- Behavior: `Load()` → `SopsFile.DecryptTree` → flatten ordered tree to `key:value` pairs: nested maps `a:b`, arrays `list:0:x` (config convention - NOT SOPS AAD), `Comment` nodes skipped, `bytes` → base64, `time` → ISO-8601, `bool` → `True/False` (document; matches SOPS canonical, not `true/false`), keys case-insensitive (BCL default).
- `WatchForChanges:true` → `FileSystemWatcher` (debounced 250ms) → `OnReload`. `Optional` + `ReloadOnChange` parity with JSON provider.
- Extensions: `AddSops(this IConfigurationBuilder, string path)`, `AddSops(this IConfigurationBuilder, Action<SopsOptions>)`, `AddSops(this IHostApplicationBuilder, ...)`.
- Tests: `ConfigurationTests.cs` - nested, arrays, case-insensitivity, multi-file overlay (last-wins), reload on file change (temp file + poll), missing-file optional vs throw.
- Gate: config tests green; `ConfigurationSample` runs and prints a secret.

### Phase 4 - Samples, Benchmarks, Docs, Release (1–2 days)

**Step X - Samples.** `samples/MinimalApiSample/Program.cs` (`builder.Configuration.AddSops(...)` + `/secret` endpoint in dev only) + `samples/ConfigurationSample/`. Both must `dotnet run` with test fixture (never prod keys). Gate: READMEs with exact commands.

**Step Y - Benchmarks + docs.** `benchmarks/Nops.Benchmarks/` (BenchmarkDotNet: YAML small/large, JSON, age vs PGP data-key unwrap, cold vs warm). Docs: `docs/quickstart.md`, `docs/configuration.md` (array/case rules), `docs/cloud-kms.md` (per-provider auth), `docs/test-keys.md` (hygiene). Gate: benchmarks run once, numbers pasted (no unevidenced "10x faster" claims).

**Step Z - Packaging + CI + release.**
- `dotnet pack -c Release` → `Nops.Core.*.nupkg` + `Nops.Configuration.*.nupkg`; symbols (`snupkg`); README + LICENSE in package; version via `Directory.Build.props` (`1.0.0-preview.1`).
- CI (`.github/workflows/ci.yml`): `dotnet build` + `dotnet test` + `sops decrypt` diff job + `dotnet list package` no-cloud-refs assert on `Nops.Core` + `dotnet format --verify-no-changes`.
- Release: signed tag, changelog from conventional commits, NuGet push (manual approval).
- Final gate: clean clone → `dotnet test` green → `dotnet pack` succeeds → samples run.

---

## Key Design Decisions

### 1. Decryption-Only (MVP)
Apps read encrypted secrets at startup - they don't typically write them back. Encryption is a CI/CD concern (`sops -e` in pipeline). If encryption is needed later, it's the inverse of decryption (encrypt values → re-encrypt data key with all master keys → regenerate MAC), but not part of MVP scope.

### 2. AES-GCM Backend TBD by Spike (not assumed S.S.C.)
- Upstream uses 32-byte nonces (`nonceSize=32`). .NET `AesGcm` historically accepts only 12-byte nonces.
- **Phase 0 decides:** S.S.C. if it accepts 32B nonces on net8.0+, else BouncyCastle `GcmBlockCipher` for value decryption. Either way pure managed, no native deps.
- **Contract (both backends must satisfy):** `Decrypt(key[32], nonce[32], ciphertext, tag[16], aadUtf8) → plaintext`. Ciphertext+tag split: `data=out[:len-16]`, `tag=out[len-16:]`. AAD is UTF-8 bytes of `Join(path,":")+":"`. Never truncate/pad the nonce to 12 bytes.
- **Age:** AgeSharp for X25519/scrypt/ChaCha20-Poly1305 stanza decrypt. Wire as `AgeMasterKey`. Do NOT call `AgeSharp.AgeKeygen` unless it exists in the pinned version - discover first.
- **PGP:** BouncyCastle.Cryptography (pinned 2.x). Ed25519/curve25519 support is limited - throw a clear error for unsupported key types, don't fallback to subprocess.
- **Shamir:** port `sops/shamir/shamir.go` GF(2^8) `Split`/`Combine` exactly. Do NOT use a random NuGet Shamir lib or invent polynomial math. Multi-group decrypt requires ≥ threshold shares; single-group short-circuits on first success.

### 3. Ordered Tree First, Dictionary Last
MAC and AAD both depend on walk order and path. Internal model is ordered `TreeBranch`; flattened `IDictionary` / `T` binding happens only after successful MAC verify. Never hash or decrypt from a `Dictionary`.

### 4. Cloud KMS = Separate NuGets
```xml
<!-- Only if using AWS -->
<PackageReference Include="Nops.Core.AwsKms" Version="1.0.0" />
```
No conditional refs inside one package - separate packages per provider.

### 5. Caller Supplies Keys (No Auto-Discovery)
- **Age:** Caller passes identities (multiple) or file paths.
- **PGP:** Caller provides private key bytes or file paths (+ passphrases or explicit unsupported error).
- **AWS KMS:** AWS SDK default chain (env, IMDS, shared config); caller may inject `IAmazonKMS`.
- **GCP KMS:** ADC.
- **Azure KV:** `DefaultAzureCredential`.
No magic - explicit is better for security-sensitive code. No remote `keyservice` (gRPC) - out of scope for in-process library.

### 6. Configuration Provider = Opt-In
Core package (`Nops.Core`) has zero Microsoft.Extensions dependencies. Configuration integration lives in separate `Nops.Configuration` NuGet. Users who don't need ASP.NET Core get a lean package.

---

## Milestones & Effort

| Phase | Steps | Deliverable | Effort |
|---|---|---|---|
| **0** | A–D | Toolchain pinned, GCM backend decided, YAML/JSON order proven, Age/PGP APIs discovered | 0.5–1 day |
| **1** | E–R | `Nops.Core` + all stores + Shamir + Age/PGP + MAC + fixtures + `dotnet pack` | 4–5 days |
| **2** | S–V | `Nops.Core.AwsKms/GcpKms/Azkv/Vault` (one PR each, emulator/live tests) | 4–6 days (1–2/provider) |
| **3** | W | `Nops.Configuration` + watch + samples wiring | 2 days |
| **4** | X–Z | Samples, benchmarks, docs, CI, signed NuGet release | 1–2 days |

**Total: ~12–16 working days. Give an agent one letter at a time (A, then B, …); each letter ends with its Gate + `dotnet test` log.**

---

## Test Strategy (TDD - mandatory, not optional)

### TDD workflow (agent must follow per component)

1. **Red:** write/extend the xUnit test first from the truth tables/vectors in this plan. Run `dotnet test --filter <Name>` - it must FAIL (or not compile). Paste the failure.
2. **Green:** implement the smallest production code to pass. No extra features, no refactoring of unrelated files.
3. **Refactor:** clean up with tests green. Re-run the full suite (`dotnet test`) - zero failures allowed before moving on.
4. One component at a time, in this order: `ToBytes` → `EncryptedValueParser` → `TreeModel/Walker+shouldBeEncrypted` → `AesGcmDecryptor` (spike vector) → `SopsMetadata` → `DataKeyResolver` (incl. Shamir) → `Age/PGP` → stores → `SopsFile` → Configuration provider. Do not skip ahead to stores/KMS before walker+MAC tests are green.
5. Every PR/task ends with: `dotnet test` log + `sops decrypt` diff on fixtures (see Cross-Verification). No log, no merge.

### Per-phase gates

- **Phase 0:** spike console/test prints decrypted `example.yaml: myapp1` value + backend decision recorded in Decision 2. Gate: test green or spike rejected with pasted exception.
- **Phase 1:** all categories 1–6 + 8 green; `dotnet test` full pass; tamper test produces `MacMismatch`.
- **Phase 2:** per-provider live/emulator decrypt test green (no mocks-only). Each provider is its own PR.
- **Phase 3:** configuration tests green incl. `a:b`, `list:0:x` (config flatten - not SOPS AAD), case-insensitivity, `WatchForChanges` reload.
- **Phase 4:** `dotnet pack` succeeds, samples run, benchmarks run once (no perf regression claim without numbers).

### Test-file mapping (exact - create these, don't improvise names)

```
tests/Nops.Core.Tests/Crypto/ToBytesTests.cs
tests/Nops.Core.Tests/Crypto/AesGcmTests.cs
tests/Nops.Core.Tests/Crypto/ParserTests.cs
tests/Nops.Core.Tests/Crypto/ShamirTests.cs
tests/Nops.Core.Tests/WalkerTests.cs
tests/Nops.Core.Tests/MetadataTests.cs
tests/Nops.Core.Tests/KeyResolverTests.cs
tests/Nops.Core.Tests/Stores/YamlStoreTests.cs
tests/Nops.Core.Tests/Stores/JsonStoreTests.cs
tests/Nops.Core.Tests/Stores/DotenvStoreTests.cs
tests/Nops.Core.Tests/Stores/IniStoreTests.cs
tests/Nops.Core.Tests/Stores/BinaryStoreTests.cs
tests/Nops.Core.Tests/MacTests.cs
tests/Nops.Core.Tests/SopsFileTests.cs
tests/Nops.Configuration.Tests/ConfigurationTests.cs
```

Coverage expectation: walker + `ToBytes` + parser at 100% branches (they're the compat surface). Overall ≥ 85% lines before Phase 2.

### Fixture Files (committed to repo, TEST-ONLY keys only)
```
tests/Nops.Core.Tests/Fixtures/
├── secrets.sops.yaml          # Encrypted with age (test identity, DO NOT reuse prod keys)
├── secrets.sops.json          # Encrypted with PGP (test key, no passphrase + one with passphrase)
├── nested.sops.yaml           # Deep nesting + arrays test (AAD path coverage)
├── dotenv.sops.env            # Dotenv format (sops_* flatten)
├── ini.sops.ini               # INI format (sections)
├── binary.sops.json           # Binary format ({data: ENC[...]})
├── multi-keygroup.sops.yaml   # Shamir secret sharing (2-of-3) + shamir_threshold
├── mac-only-encrypted.sops.yaml  # mac_only_encrypted=true vector
└── comments.sops.yaml         # type:comment + comment-in-sequence (getsops/sops#2243)
```

Label every private key `TEST ONLY - NOT FOR PRODUCTION`.

### Test Categories
1. **Crypto tests** - AES-GCM roundtrip with 32B nonce (both backends if shimmed), `ToBytes` vectors per type (esp. `True/False`, float formatting), age stanza unwrap, PGP decrypt.
2. **Parser tests** - upstream regex (no `$`), base64 `StdEncoding`, `isEmpty`, all 7 types, malformed inputs.
3. **Walker tests** - `shouldBeEncrypted` matrix (suffix × regex × comment-regex), AAD path strings incl. arrays, comment keys/values.
4. **Store tests** - YAML/JSON/Dotenv/INI/Binary parse → extract metadata → decrypt → emit round-trip; order preservation.
5. **MAC tests** - decrypt-then-verify, `mac_only_encrypted` seed, comment-in-sequence, tamper → `MacMismatch`.
6. **Integration tests** - Load fixture with known keys → verify all values match plaintext.
7. **Configuration tests** - `AddSops()` → read via `IConfiguration` (`a:b`, `list:0:x`, case-insensitive) → compare expected values.
8. **Edge cases** - empty values, special chars in keys, nested arrays, binary data, `bytes`/`time` types.

### Cross-Verification
Run SOPS CLI (`sops decrypt`) on fixture files and diff output against Nops to verify correctness. Add Go-parity fuzz for `shouldBeEncrypted` + `ToBytes` if time permits.
