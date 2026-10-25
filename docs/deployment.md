# Deploying with Nops

How to lay out configuration, ship the age key, and run Nops in containers and CI.

## Recommended layout: plain config plus one secrets file

Keep everything reviewable in plaintext and put only the secrets in a SOPS file:

```text
appsettings.json        # hosts, ports, feature flags: plaintext, easy to diff
secrets.sops.yaml       # passwords, API keys: the only encrypted file
```

```csharp
builder.Configuration
    .AddSops("secrets.sops.yaml")      // overrides appsettings.json
    .AddEnvironmentVariables()         // still wins over both
    .AddCommandLine(args);
```

Later sources win, so the precedence is `appsettings.json` < SOPS file < environment variables < command line.
[`samples/LayeredConfigSample`](https://github.com/atulmish/Nops/tree/main/samples/LayeredConfigSample) runs this and prints which source supplied each value.

Per-environment secrets are one file per environment (`secrets.prod.sops.yaml`, ...), each encrypted to that environment's key, with the file name chosen at startup:

```csharp
builder.Configuration.AddSops($"secrets.{builder.Environment.EnvironmentName.ToLowerInvariant()}.sops.yaml");
```

### Encrypting only some values

By default sops encrypts every value and leaves the keys readable. To encrypt only the sensitive ones, set a rule in `.sops.yaml`:

```yaml
creation_rules:
  - path_regex: appsettings\.sops\.json$
    age: age1...
    encrypted_regex: '^(Password|Key|ConnectionString)$'   # or unencrypted_regex to exclude instead
```

The regex is matched against key names at every level, and a matching key encrypts everything beneath it. Nops honours these rules, so a partly encrypted `appsettings.sops.json` loads like any other file. The file's MAC covers the plaintext values too, so editing one by hand fails with a `MacMismatchException`; change it with `sops edit`.
An allowlist silently leaves a secret in plaintext if it has an unexpected name, so prefer `unencrypted_regex` when in doubt.

## Providing the key

Nops looks for age keys the way the sops CLI does: `SOPS_AGE_KEY`, then `SOPS_AGE_KEY_FILE`, then `~/.config/sops/age/keys.txt`. In production, give the process a **file** and point `SOPS_AGE_KEY_FILE` at it. Prefer that to `SOPS_AGE_KEY`, because environment variables show up in `docker inspect`, `/proc/<pid>/environ` and crash dumps.
Never bake a key into an image.

With a cloud KMS there is no key to ship: the workload's identity decrypts the data key. See [Cloud KMS setup](cloud-kms.md).

### Kubernetes

Mount the age key from a Secret, and set `SOPS_AGE_KEY_FILE`. The encrypted secrets file can ship in the image or come from a ConfigMap: it is safe to store because it is encrypted.

```yaml
apiVersion: apps/v1
kind: Deployment
metadata: { name: myapp }
spec:
  template:
    spec:
      containers:
        - name: myapp
          image: myapp:1.0.0
          env:
            - name: SOPS_AGE_KEY_FILE
              value: /run/secrets/age/keys.txt
          volumeMounts:
            - { name: age-key, mountPath: /run/secrets/age, readOnly: true }
      volumes:
        - name: age-key
          secret: { secretName: myapp-age-key }
```

The container does not need the `sops` binary, a shell or an init step that writes decrypted files.

### Docker

```bash
docker run --rm -v "$PWD/keys.txt:/run/secrets/age.txt:ro" \
  -e SOPS_AGE_KEY_FILE=/run/secrets/age.txt myapp
```

### CI/CD

The runtime needs no `sops`. Install it in CI only for the steps that create or edit files. If your pipeline currently runs `sops -d` and mounts the plaintext result, replace that step with the encrypted file plus the key mount above, so plaintext never lands on disk.

## Rotating secrets and keys

- **Change a secret:** `sops edit secrets.sops.yaml`, commit, redeploy.
- **Add or remove a recipient:** edit `.sops.yaml`, then `sops updatekeys secrets.sops.yaml`.
- **Rotate the data key:** `sops rotate -i secrets.sops.yaml`.

With `WatchForChanges = true`, Nops reloads when the file changes. That covers in-place rewrites, atomic replacements and the symlink swap Kubernetes uses to update mounted Secret and ConfigMap volumes, so a rotated secrets file reaches `IOptionsMonitor<T>` without restarting the pod. The change is picked up after `ReloadInterval` (250 ms by default).

If a reload fails (bad key, half-written or tampered file), the previous values stay in place. Set `OnReloadError` to log it:

```csharp
builder.Configuration.AddSops("secrets.sops.yaml", opts =>
{
    opts.WatchForChanges = true;
    opts.OnReloadError = ex => logger.LogError(ex, "SOPS reload failed; keeping the previous values");
});
```

Values already bound with `IOptions<T>` are read once; use `IOptionsMonitor<T>` or `IOptionsSnapshot<T>` to see reloads.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `Cannot decrypt: none of the 0 configured master key(s) could decrypt the data key. The file is encrypted for: age x1...` | No usable key: check `SOPS_AGE_KEY_FILE` and `AgeIdentities`, and that your key is one of the file's recipients (the `age:` entries in its `sops:` section) |
| `MacMismatchException` | The file was edited without sops, or truncated. Restore it and use `sops edit` |
| `Unknown file extension '.enc' - specify FileKind explicitly` | Nops detects `.yaml`, `.yml`, `.json`, `.env`, `.dotenv`, `.ini`, `.bin`. Set `opts.FileKind` for anything else |
| `FileNotFoundException: SOPS file not found` at startup | The path is resolved against the **current working directory**, not the app folder (unlike `AddJsonFile`, which uses the content root). Use `Path.Combine(AppContext.BaseDirectory, "secrets.sops.yaml")` for services that start elsewhere |
| A secret is missing although the file is there | The file is marked `Optional = true`. That only tolerates a **missing file**: check the path (relative paths resolve against the working directory) and that the file is mounted where you expect |
