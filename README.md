# Kubernetes.AspNetCore.DataProtection

[![Nuget](https://img.shields.io/nuget/vpre/Kubernetes.AspNetCore.DataProtection.svg?style=flat-square)](https://www.nuget.org/packages/Kubernetes.AspNetCore.DataProtection)
[![Nuget)](https://img.shields.io/nuget/dt/Kubernetes.AspNetCore.DataProtection.svg?style=flat-square)](https://www.nuget.org/packages/Kubernetes.AspNetCore.DataProtection)
[![codecov](https://codecov.io/gh/IvanJosipovic/Kubernetes.AspNetCore.DataProtection/branch/alpha/graph/badge.svg?token=EYFpBdUvgb)](https://codecov.io/gh/IvanJosipovic/Kubernetes.AspNetCore.DataProtection)

Support for storing AspNetCore DataProtection keys using Kubernetes Secrets. 

## How to use

```csharp
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
                    x.XmlRepository = new KubernetesSecretXmlRepository(client, "myapp", "default");
                }
            });
    }
}
```
