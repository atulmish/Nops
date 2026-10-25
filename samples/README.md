# Samples

Each sample runs as-is with `dotnet run`. They are encrypted to the repository's **TEST-ONLY** age key
(`tests/Nops.Core.Tests/Fixtures/AGE-TEST-KEY.txt`), and each `launchSettings.json` points
`SOPS_AGE_KEY_FILE` at it. Never use that key for real secrets.

| Sample | Shows |
|---|---|
| [`ConfigurationSample`](ConfigurationSample) | Generic host: SOPS secrets bound to validated, typed options |
| [`MinimalApiSample`](MinimalApiSample) | ASP.NET Core minimal API: reload on change with `IOptionsMonitor<T>`, never returning secret values |
| [`LayeredConfigSample`](LayeredConfigSample) | The recommended layout: plaintext `appsettings.json` plus one secrets file, with the source of every value printed |

`.sops.yaml` here holds the creation rules used to encrypt the sample files. To change a secret, run `sops edit` on the sample's `secrets.sops.yaml` with the test key available:

```bash
SOPS_AGE_KEY_FILE=../tests/Nops.Core.Tests/Fixtures/AGE-TEST-KEY.txt sops edit LayeredConfigSample/secrets.sops.yaml
```

To use a sample in your own app, replace the `ProjectReference` with `<PackageReference Include="Nops.Configuration" Version="1.0.0" />`.
