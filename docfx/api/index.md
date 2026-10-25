# API reference

Everything public in the Nops packages, generated from the XML documentation.

| Namespace | Package | Start with |
|---|---|---|
| <xref:Nops.Core> | `Nops.Core` | <xref:Nops.Core.SopsFile>, <xref:Nops.Core.SopsOptions> |
| <xref:Nops.Core.Keys> | `Nops.Core` and the KMS packages | <xref:Nops.Core.Keys.IMasterKeyProvider>, <xref:Nops.Core.Keys.AgeMasterKey>, <xref:Nops.Core.Keys.PgpMasterKey> |
| <xref:Nops.Configuration> | `Nops.Configuration` | <xref:Nops.Configuration.ConfigurationBuilderExtensions>, <xref:Nops.Configuration.SopsConfigurationOptions> |

The key providers for AWS KMS, Google Cloud KMS, Azure Key Vault and Vault Transit live in `Nops.Core.Keys` too, one class per package.
