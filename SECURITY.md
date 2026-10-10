# Security Policy

## Supported versions

| Version | Supported |
| ------- | --------- |
| 1.x     | Yes       |
| < 1.0   | No        |

## Reporting a vulnerability

Nops is a cryptographic library that handles secret material, so we take security reports seriously.

Please report vulnerabilities privately through [GitHub private security advisories](https://github.com/atulmish/Nops/security/advisories/new). Do not open a public issue.

We aim to acknowledge reports within 5 business days and will keep you informed of the progress of a fix. Please do not publish details until a fix is released.

## Scope

In scope:

- Incorrect decryption or MAC verification that could allow tampered files to be accepted
- Handling of key material (age, PGP, cloud KMS credentials) in memory or logs
- Parsing vulnerabilities (denial of service, memory corruption) in the SOPS format readers

Out of scope:

- Vulnerabilities in upstream dependencies (report them to the respective projects)
- Attacks that require already-compromised key material or filesystem access
