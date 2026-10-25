# Vault fixtures

`vault-transit.sops.yaml` was written by real `sops` 3.13.3 against a throwaway mock of Vault's transit
`encrypt` endpoint (`vault:v1:<base64 of the data key>`), so it has sops' real `hc_vault` metadata layout
(`vault_address`, `engine_path`, `key_name`, `enc`, `created_at`). The "Vault" is a mock and the file holds
no real secrets. `VaultTransitMasterKeyTests` decrypts it end to end through a mock `HttpClient`.
