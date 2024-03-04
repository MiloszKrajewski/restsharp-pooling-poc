using System;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.ObjectPool;
using RestSharp;

namespace RestSharpStress.Client1;

/// <summary>
/// PooledRestClient is just a RestClient that uses an ObjectPool to manage the HttpMessageHandler
/// instances. It exposes <see cref="Acquire"/> factory method instead of a constructor, to make it
/// clear that the instances might not be really new. It has internal static pool and keeps track
/// of handlers so they can be returned to the pool when PooledRestClient is disposed.
/// </summary>
public class PooledRestClient: RestClient
{
	private static readonly HttpMessageHandlerPool Pool = new(TimeSpan.FromMinutes(15), 1024);

	private HttpMessageHandler? _handler;

	public static RestClient Acquire(ConfigureRestClient configure) =>
		new PooledRestClient(configure, Pool.Get());

	private PooledRestClient(ConfigureRestClient configure, HttpMessageHandler handler):
		base(handler, false, configure)
	{
		_handler = handler;
	}

	protected override void Dispose(bool disposing)
	{
		// prevent double-disposal
		var handler = Interlocked.Exchange(ref _handler, null);

		// if GC decides to dispose the object (disposing is false), we cannot be sure
		// if handler is still reusable, so we are not returning it
		if (disposing && handler is not null)
		{
			Pool.Return(handler);
		}

		base.Dispose(disposing);
	}
}

/// <summary>
/// This class might not exist at all, but extracted it so we can easily have many pools,
/// configured separately (and have slightly less typing in the main class).
/// </summary>
/// <param name="maximumRetained">Maximum number of handlers to keep in the pool.</param>
internal class HttpMessageHandlerPool(TimeSpan maximumAge, int maximumRetained):
	DefaultObjectPool<HttpMessageHandler>(
		new HttpMessageHandlerPoolPolicy(maximumAge),
		maximumRetained);

internal class HttpMessageHandlerPoolPolicy:
	IPooledObjectPolicy<HttpMessageHandler>
{
	private static long _created;
	private readonly TimeSpan _maximumAge;

	public HttpMessageHandlerPoolPolicy(TimeSpan maximumAge) =>
		_maximumAge = maximumAge;

	/// <summary>Tracking number of handlers created, for debugging purposes only.</summary>
	public static long Created => Interlocked.Read(ref _created);

	public HttpMessageHandler Create()
	{
		Interlocked.Increment(ref _created);
		return new TimestampedHttpMessageHandler(new HttpClientHandler());
	}

	public bool Return(HttpMessageHandler handler) =>
		handler is not TimestampedHttpMessageHandler timestamped ||
		timestamped.Age <= _maximumAge;
}

public class TimestampedHttpMessageHandler(HttpMessageHandler handler):
	DelegatingHandler(handler)
{
	private readonly DateTime _timestamp = DateTime.UtcNow;

	public TimeSpan Age => DateTime.UtcNow - _timestamp;
}
