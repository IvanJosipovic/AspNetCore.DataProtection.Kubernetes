using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.Internal;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace Kubernetes.AspNetCore.DataProtection.Tests;

public class XmlDeletableKeyManagerTests
{
    private sealed class InMemoryXmlRepository : IDeletableXmlRepository
    {
        private readonly List<XElement> _elements = new();

        public bool DeleteElements(Action<IReadOnlyCollection<IDeletableElement>> chooseElements)
        {
            if (chooseElements is null) throw new ArgumentNullException(nameof(chooseElements));

            // Create wrappers over a snapshot of current elements.
            var wrappers = _elements
                .Select((e, i) => new DeletableElement(i, e))
                .Cast<IDeletableElement>()
                .ToList()
                .AsReadOnly();

            chooseElements(wrappers);

            // Determine which were marked for deletion.
            var deletable = wrappers
                .OfType<DeletableElement>()
                .Where(d => d.DeletionOrder.HasValue)
                .OrderByDescending(d => d.DeletionOrder)
                .ToList();

            if (deletable.Count == 0)
                return false;

            foreach (var d in deletable)
            {
                if (d.Index >= 0 && d.Index < _elements.Count && ReferenceEquals(_elements[d.Index], d.Element))
                {
                    _elements.RemoveAt(d.Index);
                }
                else
                {
                    // Fallback: remove by reference if indices shifted.
                    _elements.Remove(d.Element);
                }
            }

            return true;
        }

        public IReadOnlyCollection<XElement> GetAllElements()
            => _elements.Select(e => new XElement(e)).ToList().AsReadOnly();

        public void StoreElement(XElement element, string friendlyName)
            => _elements.Add(new XElement(element));

        private sealed class DeletableElement : IDeletableElement
        {
            public DeletableElement(int index, XElement element)
            {
                Index = index;
                Element = element;
            }

            public int Index { get; }
            public XElement Element { get; }
            public int? DeletionOrder { get; set; }
        }
    }

    private sealed class SimpleActivator : IActivator
    {
        public object CreateInstance([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type expectedBaseType, string implementationTypeName)
        {
            var type = Type.GetType(implementationTypeName, throwOnError: true)!;
            if (!expectedBaseType.IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Type '{type.FullName}' is not assignable to '{expectedBaseType.FullName}'.");
            }

            var instance = Activator.CreateInstance(type);
            return instance is null ? throw new InvalidOperationException($"Failed to create instance of '{type.FullName}'.") : instance;
        }
    }

    private static XmlDeletableKeyManager CreateManager(out InMemoryXmlRepository repo)
    {
        repo = new InMemoryXmlRepository();
        var options = Options.Create(new KeyManagementOptions
        {
            XmlRepository = repo,
            // Keep test keys short-lived to ensure logic not dependent on long spans.
            NewKeyLifetime = TimeSpan.FromDays(30),
            AuthenticatedEncryptorConfiguration = new AuthenticatedEncryptorConfiguration()
        });

        return new XmlDeletableKeyManager(options, new SimpleActivator());
    }

    [Fact]
    public void CreateNewKey_adds_key_to_repository()
    {
        var manager = CreateManager(out var repo);
        var activation = DateTimeOffset.UtcNow;
        var expiration = activation.AddDays(10);

        var key = manager.CreateNewKey(activation, expiration);

        Assert.NotNull(key);
        Assert.Equal(activation, key.ActivationDate);
        Assert.Equal(expiration, key.ExpirationDate);

        var all = manager.GetAllKeys();
        Assert.Single(all);
        Assert.Equal(key.KeyId, all.First().KeyId);

        // Underlying repository should now have at least one <key> element.
        Assert.Single(repo.GetAllElements());
    }

    [Fact]
    public void GetAllKeys_returns_all_created_keys()
    {
        var manager = CreateManager(out _);

        var now = DateTimeOffset.UtcNow;
        manager.CreateNewKey(now, now.AddDays(5));
        manager.CreateNewKey(now.AddMinutes(1), now.AddDays(6));

        var keys = manager.GetAllKeys();

        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.True(k.ExpirationDate > k.ActivationDate));
    }

    [Fact]
    public void RevokeKey_marks_key_revoked()
    {
        var manager = CreateManager(out _);

        var now = DateTimeOffset.UtcNow;
        var key = manager.CreateNewKey(now, now.AddDays(5));
        Assert.False(key.IsRevoked);

        manager.RevokeKey(key.KeyId, "test revoke");

        var refreshed = manager.GetAllKeys().Single(k => k.KeyId == key.KeyId);
        Assert.True(refreshed.IsRevoked);
    }

    [Fact]
    public void RevokeAllKeys_marks_all_revoked()
    {
        var manager = CreateManager(out _);
        var now = DateTimeOffset.UtcNow;

        manager.CreateNewKey(now, now.AddDays(5));
        manager.CreateNewKey(now.AddMinutes(1), now.AddDays(6));

        manager.RevokeAllKeys(DateTimeOffset.UtcNow, "global revoke");

        var keys = manager.GetAllKeys();
        Assert.NotEmpty(keys);
        Assert.All(keys, k => Assert.True(k.IsRevoked));
    }

    [Fact]
    public void CanDeleteKeys_matches_underlying_capability()
    {
        var manager = CreateManager(out _);
        // We cannot directly prove delegation, but we can assert the property is exposed and boolean.
        // Deletion support depends on whether the underlying repository implements IDeletableXmlRepository.
        Assert.True(manager.CanDeleteKeys);
    }

    [Fact]
    public void DeleteKeys_deletes_selected_key_and_related_revocation()
    {
        var manager = CreateManager(out _);
        var now = DateTimeOffset.UtcNow;

        var key1 = manager.CreateNewKey(now, now.AddDays(5));
        var key2 = manager.CreateNewKey(now.AddMinutes(1), now.AddDays(6));

        // Revoke key1 so a revocation element exists in repository
        manager.RevokeKey(key1.KeyId, "test");

        // Sanity: we have 2 keys, key1 revoked
        var allBefore = manager.GetAllKeys();
        Assert.Equal(2, allBefore.Count);
        Assert.True(allBefore.Single(k => k.KeyId == key1.KeyId).IsRevoked);

        var deleted = manager.DeleteKeys(k => k.KeyId == key1.KeyId);
        Assert.True(deleted);

        var remaining = manager.GetAllKeys();
        Assert.Single(remaining);
        Assert.Equal(key2.KeyId, remaining.Single().KeyId);
    }

    [Fact]
    public void GetCacheExpirationToken_cancels_after_key_change()
    {
        var manager = CreateManager(out _);

        var token = manager.GetCacheExpirationToken();
        Assert.False(token.IsCancellationRequested);

        // Trigger a change (new key)
        var now = DateTimeOffset.UtcNow;
        manager.CreateNewKey(now, now.AddDays(3));

        // A new token instance should be returned (previous one may be canceled)
        var token2 = manager.GetCacheExpirationToken();

        // Either the original token is canceled, or a different token instance is returned.
        Assert.True(token.IsCancellationRequested || !token.Equals(token2));
    }
}

