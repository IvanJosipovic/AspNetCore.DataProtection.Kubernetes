using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using k8s;
using k8s.Models;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace AspNetCore.DataProtection.Kubernetes;

/// <summary>
/// Support for storing DataProtection keys using Kubernetes Secrets
/// </summary>
public sealed class KubernetesSecretXmlRepository : IDeletableXmlRepository
{
    private readonly IKubernetes _k8s;
    private readonly string _namespace;
    private readonly string _appName;
    private readonly string _labelSelector;

    private const string AppLabelKey = "app";
    private const string TypeLabelKey = "type";
    private const string TypeLabelValue = "DataProtection";
    private const string SecretKeyName = "key.xml";

    /// <summary>
    /// Support for storing DataProtection keys using Kubernetes Secrets
    /// </summary>
    /// <param name="k8s">Kubernetes client instance used to interact with the cluster.</param>
    /// <param name="namespace">Target Kubernetes namespace for storing secrets.</param>
    /// <param name="appName">Value used in the label selector to identify secrets for the application.</param>
    public KubernetesSecretXmlRepository(IKubernetes k8s, string @namespace, string appName)
    {
        _k8s = k8s;
        _namespace = @namespace;
        _appName = appName;
        _labelSelector = $"{AppLabelKey}={appName},{TypeLabelKey}={TypeLabelValue}";
    }

    /// <summary>
    /// Get All Elements
    /// </summary>
    /// <returns></returns>
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var list = _k8s.CoreV1.ListNamespacedSecretWithHttpMessagesAsync(
            namespaceParameter: _namespace,
            labelSelector: _labelSelector).ConfigureAwait(false).GetAwaiter().GetResult();

        if (list?.Body?.Items == null)
        {
            return [];
        }

        var elements = new List<XElement>();

        foreach (var s in list.Body.Items)
        {
            try
            {
                var xml = Encoding.UTF8.GetString(s.Data[SecretKeyName]);
                elements.Add(XElement.Parse(xml));
            }
            catch (DecoderFallbackException)
            {
                Debug.WriteLine($"Failed to decode UTF-8 from secret {s.Metadata?.Name} in namespace {_namespace}");
            }
            catch (XmlException)
            {
                Debug.WriteLine($"Failed to parse XML from secret {s.Metadata?.Name} in namespace {_namespace}");
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
                Name = $"{TypeLabelValue}-{friendlyName}",
                NamespaceProperty = _namespace,
                Labels = new Dictionary<string, string>
                {
                    [AppLabelKey] = _appName,
                    [TypeLabelKey] = TypeLabelValue
                }
            },
            Data = new Dictionary<string, byte[]>
            {
                [SecretKeyName] = Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting))
            },
            Type = "Opaque"
        };

        _k8s.CoreV1.CreateNamespacedSecretWithHttpMessagesAsync(secret, _namespace).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Deletes selected DataProtection key elements from Kubernetes Secrets.
    /// </summary>
    /// <param name="chooseElements">A callback that receives the list of deletable elements and selects which to delete.</param>
    /// <returns>True if any elements were deleted; otherwise, false.</returns>
    public bool DeleteElements(Action<IReadOnlyCollection<IDeletableElement>> chooseElements)
    {
        var list = _k8s.CoreV1.ListNamespacedSecretWithHttpMessagesAsync(
            namespaceParameter: _namespace,
            labelSelector: _labelSelector).ConfigureAwait(false).GetAwaiter().GetResult();

        var deletableElements = new List<KubernetesDeletableElement>();

        foreach (var s in list.Body.Items)
        {
            deletableElements.Add(new KubernetesDeletableElement(s));
        }

        chooseElements(deletableElements);

        var anyDeleted = false;

        foreach (var element in deletableElements
            .Where(e => e.DeletionOrder.HasValue)
            .OrderBy(e => e.DeletionOrder.GetValueOrDefault()))
        {
            _k8s.CoreV1.DeleteNamespacedSecretWithHttpMessagesAsync(
                name: element.SecretName,
                namespaceParameter: _namespace
            ).ConfigureAwait(false).GetAwaiter().GetResult();

            anyDeleted = true;
        }

        return anyDeleted;
    }

    private sealed class KubernetesDeletableElement : IDeletableElement
    {
        /// <summary>
        /// Kubernetes Secret Name
        /// </summary>
        public string SecretName { get; }

        /// <inheritdoc/>
        public XElement Element { get; }

        /// <inheritdoc/>
        public int? DeletionOrder { get; set; }

        public KubernetesDeletableElement(V1Secret secret)
        {
            SecretName = secret.Metadata.Name;

            Element = XElement.Parse(Encoding.UTF8.GetString(secret.Data[SecretKeyName]));
        }
    }
}
