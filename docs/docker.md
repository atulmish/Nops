# Docker and distroless images

Because Nops decrypts inside the application, the runtime image needs no `sops` binary, no shell and no entrypoint script. That makes it a good fit for the chiseled (distroless) images Microsoft publishes for .NET.

## A minimal Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish MyApi.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /out .
ENV SOPS_AGE_KEY_FILE=/run/secrets/age/keys.txt
ENTRYPOINT ["dotnet", "MyApi.dll"]
```

Use `8.0` in place of `10.0` for .NET 8. For a worker service use the `runtime` image instead of `aspnet`. The `dotnet new nops-webapi` and `dotnet new nops-worker` templates include this Dockerfile.

The encrypted `secrets.sops.yaml` is published with the application (mark it `CopyToOutputDirectory` in the project file). The age key is **not** part of the image.

## Running with the key

```bash
docker run --rm -p 8080:8080 \
  -v "$PWD/keys.txt:/run/secrets/age/keys.txt:ro" \
  myapi
```

Chiseled images run as a non-root user, so the key file must be readable by that user. On a host with SELinux enforcing, add `z` or `Z` to the mount options (`:ro,Z`), otherwise the container sees "Access ... is denied". In Kubernetes, a mounted Secret is readable by default; see [Kubernetes and GitOps](kubernetes-gitops.md).

## What you should see

With the key mounted, the application starts and reads its secrets. Without the key, startup fails with an error that names the key types the file is encrypted for, for example:

```text
Cannot decrypt: none of the 0 configured master key(s) could decrypt the data key.
The file is encrypted for: age x1. Supply a matching key via AgeIdentities, PgpKeys or MasterKeys, ...
```

Failing at startup is deliberate: an application should not run half-configured.

## Avoiding a key file altogether

Encrypt the file for a cloud KMS key and let the container's identity (an IAM role, a managed identity or workload identity) unwrap the data key. No key is mounted. See [Cloud KMS setup](cloud-kms.md) and the [hosting guide](hosting.md).
