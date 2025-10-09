using System.Net;
using System.Text;
using System.Xml.Linq;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Moq;

namespace Kubernetes.AspNetCore.DataProtection.Tests;

public class KubernetesSecretXmlRepositoryTests
{
    private static V1Secret MakeSecret(string name, string xml) =>
        new()
        {
            Metadata = new V1ObjectMeta()
            {
                Name = name,
                NamespaceProperty = "default",
                Labels = new Dictionary<string, string>
                {
                    ["app"] = "my-app",
                    ["type"] = "dataprotection"
                }
            },
            Data = new Dictionary<string, byte[]> { { "key.xml", Encoding.UTF8.GetBytes(xml) } },
            Type = "Opaque"
        };

    [Fact]
    public void GetAllElements_reads_all_matching_secrets()
    {
        var k8s = new Mock<IKubernetes>(MockBehavior.Strict);
        var core = new Mock<ICoreV1Operations>(MockBehavior.Strict);

        k8s.SetupGet(k => k.CoreV1).Returns(core.Object);

        core.Setup(c => c.ListNamespacedSecretWithHttpMessagesAsync(
                It.Is<string>(ns => ns == "default"),
                allowWatchBookmarks: null,
                continueParameter: null,
                fieldSelector: null,
                It.Is<string>(sel => sel == "app=my-app,type=dataprotection"),
                limit: null,
                resourceVersion: null,
                resourceVersionMatch: null,
                sendInitialEvents: null,
                timeoutSeconds: null,
                watch: null,
                pretty: null,
                customHeaders: null,
                It.IsAny<CancellationToken>()
                ))
           .ReturnsAsync(new HttpOperationResponse<V1SecretList>
           {
               Body = new V1SecretList(
                   [
                    MakeSecret("dp-key-1", "<key id='1'/>"),
                    MakeSecret("dp-key-2",  "<key id='2'/>")
                   ]
               ),
               Response = new HttpResponseMessage(HttpStatusCode.OK)
           });

        var repo = new KubernetesSecretXmlRepository(k8s.Object, "default", "my-app");

        var elements = repo.GetAllElements();

        Assert.Collection(elements,
            e => Assert.Equal("1", e.Attribute("id")!.Value),
            e => Assert.Equal("2", e.Attribute("id")!.Value));

        k8s.VerifyAll();
        core.VerifyAll();
    }

    [Fact]
    public void StoreElement_creates_one_secret_with_expected_labels_and_payload()
    {
        var k8s = new Mock<IKubernetes>(MockBehavior.Strict);
        var core = new Mock<ICoreV1Operations>(MockBehavior.Strict);

        k8s.SetupGet(k => k.CoreV1).Returns(core.Object);

        V1Secret? created = null;

        core.Setup(c => c.CreateNamespacedSecretWithHttpMessagesAsync(
                It.IsAny<V1Secret>(),
                "default",
                dryRun: null,
                fieldManager: null,
                fieldValidation: null,
                pretty: null,
                customHeaders: null,
                cancellationToken: default))
               .Callback((V1Secret body,
                          string ns,
                          string? dryRun,
                          string? fieldManager,
                          string? fieldValidation,
                          bool? pretty,
                          IReadOnlyDictionary<string, IReadOnlyList<string>>? headers,
                          CancellationToken ct) =>
               {
                   created = body;
               })
               .ReturnsAsync((V1Secret body,
                          string ns,
                          string? dryRun,
                          string? fieldManager,
                          string? fieldValidation,
                          bool? pretty,
                          IReadOnlyDictionary<string, IReadOnlyList<string>>? headers,
                          CancellationToken ct)
                   => new HttpOperationResponse<V1Secret>
                   {
                       Body = body,
                       Response = new HttpResponseMessage(HttpStatusCode.OK)
                   });

        var repo = new KubernetesSecretXmlRepository(k8s.Object, "default", "my-app");

        var element = XElement.Parse("<key id='123'><data>abc</data></key>");
        repo.StoreElement(element, "friendly");

        Assert.NotNull(created);
        Assert.Equal("dataprotection-friendly", created!.Metadata.Name);
        Assert.Equal("default", created.Metadata.NamespaceProperty);
        Assert.Equal("my-app", created.Metadata.Labels["app"]);
        Assert.Equal("dataprotection", created.Metadata.Labels["type"]);
        Assert.True(created.Data!.ContainsKey("key.xml"));

        var xml = Encoding.UTF8.GetString(created.Data["key.xml"]);
        var parsed = XElement.Parse(xml);
        Assert.Equal(parsed.ToString(), element.ToString());

        k8s.VerifyAll();
        core.VerifyAll();
    }

    [Fact]
    public void Roundtrip_store_then_list_returns_stored_element()
    {
        var store = new List<V1Secret>();
        var k8s = new Mock<IKubernetes>(MockBehavior.Strict);
        var core = new Mock<ICoreV1Operations>(MockBehavior.Strict);
        k8s.SetupGet(k => k.CoreV1).Returns(core.Object);

        core.Setup(c => c.CreateNamespacedSecretWithHttpMessagesAsync(
                It.IsAny<V1Secret>(),
                "default",
                dryRun: null,
                fieldManager: null,
                fieldValidation: null,
                pretty: null,
                customHeaders: null,
                cancellationToken: default))
               .Callback((V1Secret body,
                          string ns,
                          string? dryRun,
                          string? fieldManager,
                          string? fieldValidation,
                          bool? pretty,
                          IReadOnlyDictionary<string, IReadOnlyList<string>>? headers,
                          CancellationToken ct) =>
               {
                   store.Add(body);
               })
               .ReturnsAsync((V1Secret body,
                          string ns,
                          string? dryRun,
                          string? fieldManager,
                          string? fieldValidation,
                          bool? pretty,
                          IReadOnlyDictionary<string, IReadOnlyList<string>>? headers,
                          CancellationToken ct)
                   => new HttpOperationResponse<V1Secret>
                   {
                       Body = body,
                       Response = new HttpResponseMessage(HttpStatusCode.OK)
                   });

        core.Setup(c => c.ListNamespacedSecretWithHttpMessagesAsync(
                It.Is<string>(ns => ns == "default"),
                allowWatchBookmarks: null,
                continueParameter: null,
                fieldSelector: null,
                It.Is<string>(sel => sel == "app=my-app,type=dataprotection"),
                limit: null,
                resourceVersion: null,
                resourceVersionMatch: null,
                sendInitialEvents: null,
                timeoutSeconds: null,
                watch: null,
                pretty: null,
                customHeaders: null,
                It.IsAny<CancellationToken>()
                ))
           .ReturnsAsync(new HttpOperationResponse<V1SecretList>
           {
               Body = new V1SecretList(store),
               Response = new HttpResponseMessage(HttpStatusCode.OK)
           });

        var repo = new KubernetesSecretXmlRepository(k8s.Object, "default", "my-app");

        repo.StoreElement(XElement.Parse("<key id='A'/>"), "A");
        repo.StoreElement(XElement.Parse("<key id='B'/>"), "B");
        var elements = repo.GetAllElements().ToList();

        Assert.Equal(2, elements.Count);
        Assert.Contains(elements, e => e.Attribute("id")!.Value == "A");
        Assert.Contains(elements, e => e.Attribute("id")!.Value == "B");

        k8s.VerifyAll();
        core.VerifyAll();
    }

    [Fact]
    public void DeleteElements_deletes_selected_secrets_in_requested_order()
    {
        var k8s = new Mock<IKubernetes>(MockBehavior.Strict);
        var core = new Mock<ICoreV1Operations>(MockBehavior.Strict);

        k8s.SetupGet(k => k.CoreV1).Returns(core.Object);

        core.Setup(c => c.ListNamespacedSecretWithHttpMessagesAsync(
                It.Is<string>(ns => ns == "default"),
                allowWatchBookmarks: null,
                continueParameter: null,
                fieldSelector: null,
                It.Is<string>(sel => sel == "app=my-app,type=dataprotection"),
                limit: null,
                resourceVersion: null,
                resourceVersionMatch: null,
                sendInitialEvents: null,
                timeoutSeconds: null,
                watch: null,
                pretty: null,
                customHeaders: null,
                It.IsAny<CancellationToken>()
                ))
           .ReturnsAsync(new HttpOperationResponse<V1SecretList>
           {
               Body = new V1SecretList(
                   [
                    MakeSecret("dp-key-1", "<key id='1'/>"),
                    MakeSecret("dp-key-2", "<key id='2'/>"),
                    MakeSecret("dp-key-3", "<key id='3'/>")
                   ]
               ),
               Response = new HttpResponseMessage(HttpStatusCode.OK)
           });

        var deletions = new List<string>();

        core.Setup(c => c.DeleteNamespacedSecretWithHttpMessagesAsync(
                It.IsAny<string>(),
                It.Is<string>(ns => ns == "default"),
                It.Is<V1DeleteOptions?>(body => body == null),
                It.Is<string?>(dryRun => dryRun == null),
                It.Is<int?>(grace => grace == null),
                It.Is<bool?>(ignore => ignore == null),
                It.Is<bool?>(orphan => orphan == null),
                It.Is<string?>(policy => policy == null),
                It.Is<bool?>(pretty => pretty == null),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((string name,
                      string ns,
                      V1DeleteOptions? body,
                      string? dryRun,
                      int? grace,
                      bool? ignore,
                      bool? orphan,
                      string? policy,
                      bool? pretty,
                      IReadOnlyDictionary<string, IReadOnlyList<string>>? headers,
                      CancellationToken ct) =>
            {
                deletions.Add(name);
            })
            .ReturnsAsync(new HttpOperationResponse<V1Status>
            {
                Body = new V1Status(),
                Response = new HttpResponseMessage(HttpStatusCode.OK)
            });

        var repo = new KubernetesSecretXmlRepository(k8s.Object, "default", "my-app");

        var result = repo.DeleteElements(elements =>
        {
            Assert.Equal(3, elements.Count);

            var byId = elements.ToDictionary(e => e.Element.Attribute("id")!.Value);

            byId["2"].DeletionOrder = 1;
            byId["1"].DeletionOrder = 2;
        });

        Assert.True(result);
        Assert.Equal(["dp-key-2", "dp-key-1"], deletions);

        k8s.VerifyAll();
        core.VerifyAll();
    }

    [Fact]
    public void DeleteElements_returns_false_when_no_elements_are_selected()
    {
        var k8s = new Mock<IKubernetes>(MockBehavior.Strict);
        var core = new Mock<ICoreV1Operations>(MockBehavior.Strict);

        k8s.SetupGet(k => k.CoreV1).Returns(core.Object);

        core.Setup(c => c.ListNamespacedSecretWithHttpMessagesAsync(
                It.Is<string>(ns => ns == "default"),
                allowWatchBookmarks: null,
                continueParameter: null,
                fieldSelector: null,
                It.Is<string>(sel => sel == "app=my-app,type=dataprotection"),
                limit: null,
                resourceVersion: null,
                resourceVersionMatch: null,
                sendInitialEvents: null,
                timeoutSeconds: null,
                watch: null,
                pretty: null,
                customHeaders: null,
                It.IsAny<CancellationToken>()
                ))
           .ReturnsAsync(new HttpOperationResponse<V1SecretList>
           {
               Body = new V1SecretList(
                   [
                    MakeSecret("dp-key-1", "<key id='1'/>")
                   ]
               ),
               Response = new HttpResponseMessage(HttpStatusCode.OK)
           });

        var repo = new KubernetesSecretXmlRepository(k8s.Object, "default", "my-app");

        var result = repo.DeleteElements(elements =>
        {
            Assert.Single(elements);
            Assert.Equal("1", elements.Single().Element.Attribute("id")!.Value);
        });

        Assert.False(result);

        k8s.VerifyAll();
        core.VerifyAll();
    }
}
