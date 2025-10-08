using System.Text;
using System.Xml.Linq;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace AspNetCore.DataProtection.Kubernetes;

/// <summary>
/// Support for storing DataProtection keys using Kubernetes Secrets
/// </summary>
public sealed class K8sSecretsRingRepository : IXmlRepository
{
    private readonly IKubernetes _k8s;
    private readonly string _namespace;
    private readonly string _appName;
    private readonly string _labelSelector;

    /// <summary>
    /// Support for storing DataProtection keys using Kubernetes Secrets
    /// </summary>
    /// <param name="k8s"></param>
    /// <param name="namespace"></param>
    /// <param name="appName"></param>
    public K8sSecretsRingRepository(IKubernetes k8s, string @namespace, string appName)
    {
        _k8s = k8s;
        _namespace = @namespace;
        _appName = appName;
        _labelSelector = $"app={appName},type=DataProtection";
    }

    /// <summary>
    /// Get All Elements
    /// </summary>
    /// <returns></returns>
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var list = _k8s.CoreV1.ListNamespacedSecretWithHttpMessagesAsync(
            namespaceParameter: _namespace,
            labelSelector: _labelSelector).GetAwaiter().GetResult();

        var elements = new List<XElement>();
        foreach (var s in list.Body.Items)
        {
            if (s.Data != null && s.Data.TryGetValue("key.xml", out var bytes))
            {
                var xml = Encoding.UTF8.GetString(bytes);
                elements.Add(XElement.Parse(xml));
            }
        }
        return elements;
    }

    /// <summary>
    /// Store Element
    /// </summary>
    /// <param name="element"></param>
    /// <param name="friendlyName"></param>
    public void StoreElement(XElement element, string friendlyName)
    {
        var secret = new V1Secret()
        {
            Metadata = new()
            {
                Name = $"DataProtection-{friendlyName}",
                NamespaceProperty = _namespace,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = _appName,
                    ["type"] = "DataProtection"
                }
            },
            Data = new Dictionary<string, byte[]>
            {
                ["key.xml"] = Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting))
            },
            Type = "Opaque"
        };

        _k8s.CoreV1.CreateNamespacedSecretWithHttpMessagesAsync(secret, _namespace).GetAwaiter().GetResult();
    }
}
