using System;

namespace HyperCache;

/// <summary>
/// Base exception for errors returned by the HyperCache API or raised by the HyperCache SDK.
/// </summary>
public class HyperCacheException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HyperCacheException"/> class.
    /// </summary>
    public HyperCacheException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperCacheException"/> class.
    /// </summary>
    public HyperCacheException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperCacheException"/> class.
    /// </summary>
    public HyperCacheException(string message, int? status)
        : base(message)
    {
        Status = status;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperCacheException"/> class.
    /// </summary>
    public HyperCacheException(string message, int? status, Exception? innerException)
        : base(message, innerException)
    {
        Status = status;
    }

    /// <summary>
    /// Gets the HTTP status code associated with this exception, when available.
    /// </summary>
    public int? Status { get; }
}

/// <summary>
/// Represents an authentication failure from the HyperCache API.
/// </summary>
public sealed class AuthException : HyperCacheException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthException"/> class.
    /// </summary>
    public AuthException(string message)
        : base(message, 401)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthException"/> class.
    /// </summary>
    public AuthException(string message, Exception? innerException)
        : base(message, 401, innerException)
    {
    }
}

/// <summary>
/// Represents a quota or payment-related failure from the HyperCache API.
/// </summary>
public sealed class QuotaException : HyperCacheException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QuotaException"/> class.
    /// </summary>
    public QuotaException(string message)
        : base(message, 402)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuotaException"/> class.
    /// </summary>
    public QuotaException(string message, Exception? innerException)
        : base(message, 402, innerException)
    {
    }
}

/// <summary>
/// Represents a rate-limit failure from the HyperCache API.
/// </summary>
public sealed class RateLimitException : HyperCacheException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitException"/> class.
    /// </summary>
    public RateLimitException(string message)
        : base(message, 429)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitException"/> class.
    /// </summary>
    public RateLimitException(string message, Exception? innerException)
        : base(message, 429, innerException)
    {
    }
}

/// <summary>
/// Represents a client-side API failure, usually an HTTP 4xx response.
/// </summary>
public sealed class ClientException : HyperCacheException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClientException"/> class.
    /// </summary>
    public ClientException(string message, int? status = null)
        : base(message, status)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientException"/> class.
    /// </summary>
    public ClientException(string message, int? status, Exception? innerException)
        : base(message, status, innerException)
    {
    }
}

/// <summary>
/// Represents a server-side, network, or timeout failure.
/// </summary>
public sealed class ServerException : HyperCacheException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerException"/> class.
    /// </summary>
    public ServerException(string message, int? status = null)
        : base(message, status)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerException"/> class.
    /// </summary>
    public ServerException(string message, int? status, Exception? innerException)
        : base(message, status, innerException)
    {
    }
}
