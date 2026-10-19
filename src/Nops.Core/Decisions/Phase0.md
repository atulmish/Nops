# Design Decisions - Phase 0

## Decision 1: Target Framework

**Decision:** `net10.0` (originally planned `net8.0`)

**Rationale:** System has .NET SDK 10.0.112 installed but no .NET 8.0 runtime. Tests require the matching runtime to execute test hosts. The library targets `net10.0` for development; can add `net8.0` TFM later if needed (requires installing .NET 8.0 runtime).

**Trade-off:** Slightly higher minimum version requirement, but gains access to latest BCL improvements.

---

## Decision 2: AES-GCM Backend

**Decision:** Use **BouncyCastle `GcmBlockCipher`** as the primary AES-GCM backend for value decryption.

**Rationale:** .NET's `System.Security.Cryptography.AesGcm` rejects 32-byte nonces (throws `ArgumentException`). SOPS specification uses 32-byte nonces, so we cannot use the BCL implementation directly.

**Evidence:** Spike test `AesGcmBackendSpikeTests.AesGcm_Accepts32ByteNonce` confirms rejection:

```text
System.ArgumentException: The specified nonce is not a valid size for this algorithm. (Parameter 'nonce')
```

**BouncyCastle API discovered:**

- Constructor: `GcmBlockCipher(IBlockCipher c)` - pass `AesEngine`
- Init: `Init(bool forEncryption, ICipherParameters parameters)`
- Process: `ProcessBlock(byte[] inBuf, int inOff, byte[] outBuf, int outOff)`

**Alternative considered:** Shim .NET AesGcm with 12-byte nonce by truncating/padding SOPS nonces. **Rejected** because it would break GCM authentication (AAD + nonce are cryptographically significant).

---

## Decision 3: YAML Parsing - YamlDotNet `YamlStream`

**Decision:** Use `YamlStream` → `YamlMappingNode.Children` for ordered parsing.

**Rationale:** `YamlMappingNode` exposes a `Children` property (discovered via reflection) that preserves insertion order from the source file. This is critical for SOPS MAC computation which depends on walk order.

**Key types discovered:**

- `YamlStream.Load(TextReader)` - parse YAML
- `YamlMappingNode.Children` - ordered key-value pairs
- `YamlSequenceNode` - arrays (list-like)
- `YamlScalarNode.Value` - scalar value (string)

**Note:** `YamlScalarNode.Comment` property not available in this version. Comments may need to be captured during parsing via the event stream or by using a different API.

---

## Decision 4: PGP Key Loading - BouncyCastle `ArmoredInputStream`

**Decision:** Use `Org.BouncyCastle.Bcpg.ArmoredInputStream` for reading PGP armored keys.

**Rationale:** SOPS-encrypted files contain PGP `enc` fields with ASCII-armored PGP messages. BouncyCastle's `ArmoredInputStream` is the standard way to decode these.

**Key types discovered:**

- `Org.BouncyCastle.Bcpg.ArmoredInputStream` - decode armored PGP data
- `Org.BouncyCastle.Bcpg.OpenPgp.*` - PGP message types (PgpEncryptedDataList, etc.)

---

## Decision 5: AgeSharp Library

**Decision:** Use **AgeSharp** library for age X25519 + scrypt + ChaCha20-Poly1305 stanza decryption.

**Rationale:** AgeSharp is the most mature .NET age implementation with MIT license. Will discover exact API surface in Phase 1 (Step D).

**Next steps:** Add AgeSharp package reference, list real types via reflection, create compiling snippet for age stanza decrypt.

---

## Summary of Phase 0 Deliverables

| Item | Status |
|------|--------|
| `global.json` - SDK pinned to 10.0.100 | Done |
| `Nops.sln` - solution file created | Done |
| `src/Nops.Core/Nops.Core.csproj` - net10.0, nullable, implicit usings, warnings as errors | Done |
| `tests/Nops.Core.Tests/Nops.Core.Tests.csproj` - xUnit, project ref to Core | Done |
| `dotnet build` green | Done |
| `dotnet test` (empty) green | Done |
| Decision 2: AES-GCM = BouncyCastle.GcmBlockCipher | Done |
| Decision 3: YAML = YamlDotNet YamlStream + Children | Done |
| Decision 4: PGP = ArmoredInputStream | Done |
| Decision 5: Age = AgeSharp (to be confirmed in Phase 1) | Pending |
