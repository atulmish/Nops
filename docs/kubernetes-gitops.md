# Kubernetes and GitOps

Nops and in-cluster SOPS decryption solve different halves of the same problem, and they can be combined.

## Two ways to get a secret to a pod

**Decrypt in the cluster.** A GitOps controller decrypts the SOPS file and creates a Kubernetes Secret. Flux does this natively through its kustomize-controller. Argo CD does not decrypt SOPS files itself and needs a plugin such as KSOPS or helm-secrets. The application then receives the values as environment variables or mounted files.

**Decrypt in the application.** The encrypted file is delivered to the pod unchanged, and Nops decrypts it in the process. Only the key is a Secret (or, with a cloud KMS, there is no key to deliver at all).

| | Decrypt in the cluster | Decrypt in the application with Nops |
|---|---|---|
| Plaintext secrets stored as Kubernetes Secrets | Yes | No |
| The application needs a key | No | Yes (a mounted age key, or workload identity for a KMS) |
| Secrets visible to anyone who can read Secrets in the namespace | Yes | Only the key (or nothing, with a KMS) |
| Same behaviour on a laptop, a virtual machine and in the cluster | No | Yes |
| Requires GitOps tooling | Yes | No |

## Delivering the encrypted file

The file is safe to store because it is encrypted. Either put it in the image, or mount it from a ConfigMap:

```yaml
apiVersion: v1
kind: ConfigMap
metadata: { name: myapp-secrets }
data:
  secrets.sops.yaml: |
    # paste the contents of the encrypted file here, or generate this ConfigMap from it
```

```yaml
volumeMounts:
  - { name: secrets-file, mountPath: /app/config, readOnly: true }
  - { name: age-key, mountPath: /run/secrets/age, readOnly: true }
env:
  - { name: SOPS_AGE_KEY_FILE, value: /run/secrets/age/keys.txt }
```

Generate the ConfigMap from the file instead of pasting it:

```bash
kubectl create configmap myapp-secrets --from-file=secrets.sops.yaml --dry-run=client -o yaml
```

## Reloading

With `WatchForChanges = true`, Nops sees Kubernetes ConfigMap and Secret volume updates (the kubelet swaps a `..data` symlink) and reloads without restarting the pod. See [Deploying with Nops](deployment.md#rotating-secrets-and-keys). Remember that a ConfigMap volume can take up to the kubelet sync period to refresh.

## Using both

A common arrangement is to let Flux decrypt infrastructure secrets (image pull secrets, ingress certificates) and to let applications decrypt their own configuration with Nops. The same `.sops.yaml` creation rules and the same age or KMS keys serve both.

## Workload identity instead of a key

On EKS, GKE and AKS you can avoid distributing a key. Encrypt the file for a cloud KMS key and give the pod's service account permission to use it. Nops then calls the KMS to unwrap the data key. See [Cloud KMS setup](cloud-kms.md).
