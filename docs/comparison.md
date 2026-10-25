# Nops compared with the other ways to use SOPS from .NET

SOPS is a command-line tool written in Go. A .NET application cannot read a SOPS file by itself, so teams use one of the approaches below. This page states what each one costs and when it is still the right choice.

| Approach | `sops` binary needed at runtime | Plaintext on disk or in a Kubernetes Secret | Extra deploy step | Works outside Kubernetes | Integrates with `IConfiguration` |
|---|---|---|---|---|---|
| `sops -d` in CI or an entrypoint script | In the image, or on the build agent | Yes | Yes | Yes | Only through the files it writes |
| .NET packages that wrap the `sops` and `age` command-line tools | Yes | Usually a temporary file | No | Yes | Varies |
| `dotnet-sops` (a tool for encrypting .NET user-secrets) | Yes, on the developer machine | In the local user-secrets store | No | Local development only | Through user-secrets |
| Cluster tooling (Flux decryption, KSOPS, helm-secrets) | In the cluster tooling | Yes, as Kubernetes Secrets | Yes | No | Through environment variables or mounted files |
| A runtime secret store (Azure Key Vault, AWS Secrets Manager, Vault) | No | No | No | Yes | Yes, through a provider |
| **Nops** | **No** | **No** | **No** | **Yes** | **Yes** |

## When another approach is the better choice

- **You already decrypt in the cluster with Flux and are content with Kubernetes Secrets.** Nops adds little. You may still prefer Nops for an application that also runs outside the cluster, or to keep decrypted values out of etcd. See [Kubernetes and GitOps](kubernetes-gitops.md).
- **You need to create or edit encrypted files from code.** Nops only decrypts. Use the `sops` CLI for that.
- **Your secrets must be rotated centrally and fetched on demand.** A runtime secret store is built for this. Nops is for teams that want the secrets versioned and reviewed in git.
- **You are not on .NET.** Use the `sops` binary or a library for your language.

## What Nops does not do differently

Nops reads the same files the `sops` CLI writes, supports the same key types (age, PGP, AWS KMS, GCP KMS, Azure Key Vault, HashiCorp Vault Transit) and honours the same rules (`encrypted_regex`, `unencrypted_suffix`, key groups). Your team keeps editing with `sops`. Nothing about the files in git changes.

## Notes on the comparison

The .NET wrapper packages were identified from their NuGet metadata (they depend on packages that call the `sops` and `age` executables). Behaviour differs between packages, so the "Varies" and "Usually" entries describe the common case rather than every package. If you maintain one of these tools and an entry is inaccurate, please open an issue.
