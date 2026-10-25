# NopsWebApi

This project loads its configuration from `appsettings.json` and a SOPS-encrypted `secrets.sops.yaml` using [Nops](https://github.com/atulmish/Nops). There is no `sops` binary at runtime and no plaintext secrets file.

## Run it

```bash
dotnet run
```

The launch profile points `SOPS_AGE_KEY_FILE` at `dev-age-key.txt`, a **TEST-ONLY demo key that is public**. It exists so the project runs immediately and protects nothing.

## Switch to your own key

1. Create a key and note the public key it prints:

   ```bash
   age-keygen -o keys.txt
   ```

2. Put the public key in the `age:` field of `.sops.yaml`.
3. Write your secrets to a temporary `secrets.yaml`, then encrypt them and delete the plaintext:

   ```bash
   sops -e secrets.yaml > secrets.sops.yaml && rm secrets.yaml
   ```

4. Delete `dev-age-key.txt` and point `SOPS_AGE_KEY_FILE` at `keys.txt`. Keep `keys.txt` out of git (the `.gitignore` already lists it).

Edit secrets later with `sops edit secrets.sops.yaml`.

## Run in a container

The `Dockerfile` builds with the SDK image and runs on a chiseled image with no shell. Mount the key at run time:

```bash
docker build -t nopswebapi .
docker run --rm -v "$PWD/keys.txt:/run/secrets/age/keys.txt:ro" nopswebapi
```

More: [Nops documentation](https://atulmish.github.io/Nops/), [deployment guide](https://atulmish.github.io/Nops/docs/deployment.html).
