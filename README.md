# Kubernetes.AspNetCore.DataProtection

[![Nuget](https://img.shields.io/nuget/vpre/Kubernetes.AspNetCore.DataProtection.svg?style=flat-square)](https://www.nuget.org/packages/Kubernetes.AspNetCore.DataProtection)
[![Nuget)](https://img.shields.io/nuget/dt/Kubernetes.AspNetCore.DataProtection.svg?style=flat-square)](https://www.nuget.org/packages/Kubernetes.AspNetCore.DataProtection)
[![codecov](https://codecov.io/gh/IvanJosipovic/Kubernetes.AspNetCore.DataProtection/branch/main/graph/badge.svg?token=EYFpBdUvgb)](https://codecov.io/gh/IvanJosipovic/Kubernetes.AspNetCore.DataProtection)

Support for storing AspNetCore DataProtection keys using Kubernetes Secrets. 

## How to use

```csharp
using Kubernetes.AspNetCore.DataProtection;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services
            .AddDataProtection()
            .AddKeyManagementOptions(x =>
            {
                if (KubernetesClientConfiguration.IsInCluster())
                {
                    var config = KubernetesClientConfiguration.InClusterConfig();
                    var client = new k8s.Kubernetes(config);
                    x.XmlRepository = new KubernetesSecretXmlRepository(client, "default", "myapp");
                }
            });

        builder.Services.AddSingleton<IKeyManager, XmlDeletableKeyManager>();

        var app = builder.Build();
        app.Run();
    }
}
```

## Required Permissions
This library requires Secret Create, List and Delete permissions

```yaml
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: my-app-role
rules:
- apiGroups: [""]
  resources: ["secrets"]
  verbs: ["list", "create", "delete"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: my-app-role-binding
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: my-app-role
subjects:
- kind: ServiceAccount
  name: my-app-service-account
  namespace: default
  ```
