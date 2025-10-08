using k8s;
using k8s.Models;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Text;
using System.Xml.Linq;

namespace AspNetCore.DataProtection.Kubernetes;

public sealed class K8sSecretsRingRepository : IXmlRepository
{
    private readonly IKubernetes _k8s;
    private readonly string _ns;
    private readonly string _labelSelector;

    public K8sSecretsRingRepository(IKubernetes k8s, string @namespace, string labelSelector)
    {
        _k8s = k8s;
        _ns = @namespace;
        _labelSelector = labelSelector; // e.g. "app=my-app,ring=dataprotection"
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var list = _k8s.CoreV1.ListNamespacedSecret(
            namespaceParameter: _ns,
            labelSelector: _labelSelector);

        var elements = new List<XElement>();
        foreach (var s in list.Items)
        {
            if (s.Data != null && s.Data.TryGetValue("key.xml", out var bytes))
            {
                var xml = Encoding.UTF8.GetString(bytes);
                elements.Add(XElement.Parse(xml));
            }
        }
        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        var name = $"dp-key-{Guid.NewGuid():N}";

        var secret = new V1Secret(
            metadata: new V1ObjectMeta(
                name: name,
                namespaceProperty: _ns,
                labels: new Dictionary<string, string>
                {
                    ["app"] = "my-app",
                    ["ring"] = "dataprotection"
                }),
            data: new Dictionary<string, byte[]>
            {
                ["key.xml"] = Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting))
            },
            type: "Opaque");

        _k8s.CoreV1.CreateNamespacedSecret(secret, _ns);
    }
}