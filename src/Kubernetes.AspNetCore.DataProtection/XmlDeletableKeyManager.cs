using Microsoft.AspNetCore.DataProtection.Internal;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Options;

namespace Kubernetes.AspNetCore.DataProtection;

/// <summary>
/// An <see cref="IDeletableKeyManager"/> implementation that delegates to <see cref="XmlKeyManager"/>.
/// </summary>
public sealed class XmlDeletableKeyManager : IDeletableKeyManager
{
    private readonly XmlKeyManager _xmlKeyManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDeletableKeyManager"/> class which delegates all
    /// operations to an underlying <see cref="XmlKeyManager"/>.
    /// </summary>
    /// <param name="keyManagementOptions">The key management options used to configure the underlying key manager.</param>
    /// <param name="activator">The activator used for type instantiation by the underlying key manager.</param>
    public XmlDeletableKeyManager(IOptions<KeyManagementOptions> keyManagementOptions, IActivator activator)
    {
        _xmlKeyManager = new XmlKeyManager(keyManagementOptions, activator);
    }

    /// <inheritdoc/>
    public bool CanDeleteKeys => _xmlKeyManager.CanDeleteKeys;

    /// <inheritdoc/>
    public IKey CreateNewKey(DateTimeOffset activationDate, DateTimeOffset expirationDate)
    {
        return _xmlKeyManager.CreateNewKey(activationDate, expirationDate);
    }

    /// <inheritdoc/>
    public bool DeleteKeys(Func<IKey, bool> shouldDelete)
    {
        return _xmlKeyManager.DeleteKeys(shouldDelete);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IKey> GetAllKeys()
    {
        return _xmlKeyManager.GetAllKeys();
    }

    /// <inheritdoc/>
    public CancellationToken GetCacheExpirationToken()
    {
        return _xmlKeyManager.GetCacheExpirationToken();
    }

    /// <inheritdoc/>
    public void RevokeAllKeys(DateTimeOffset revocationDate, string? reason = null)
    {
        _xmlKeyManager.RevokeAllKeys(revocationDate, reason);
    }

    /// <inheritdoc/>
    public void RevokeKey(Guid keyId, string? reason = null)
    {
        _xmlKeyManager.RevokeKey(keyId, reason);
    }
}
