using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RestSharp;

namespace RestSharpStress.Client1;

internal class Program
{
	private const int Threads = 128;

	private static readonly TaskCompletionSource<int> Start = new();
	private static readonly CancellationTokenSource Stop = new();
	private static long _finished;
	private static long _failed;

	public static void Main(string[] args)
	{
		var loops = Enumerable.Range(0, Threads).Select(_ => StartLoop()).ToArray();
		var monitor = Task.Run(Monitor);

		Start.SetResult(0);
		Console.CancelKeyPress += (_, e) => {
			Console.WriteLine("Stopping...");
			e.Cancel = true;
			Stop.Cancel();
		};

		Task.WaitAll(loops);
		monitor.Wait();

		Console.WriteLine("Stopped");
	}

	private static async Task Monitor()
	{
		await Start.Task;
		while (!Stop.IsCancellationRequested)
		{
			await Task.Delay(1000);
			Console.WriteLine(
				$"Finished: {_finished}, Failed: {_failed}, Clients: {HttpMessageHandlerPoolPolicy.Created}");
		}
	}

	private static async Task StartLoop()
	{
		while (!Stop.IsCancellationRequested)
		{
			try
			{
				_ = await GetTime();
				Interlocked.Increment(ref _finished);
			}
			catch (Exception)
			{
				Interlocked.Increment(ref _failed);
			}
		}
	}

	private static async Task<DateTime> GetTime()
	{
		// NOTE: Dispose is crucial, it is the mechanism that returns the handler to the pool
		using var client = GetClient();
		return await client.GetJsonAsync<DateTime>("/");
	}

	private static RestClient GetClient() =>
		UseOursPooledClient();

	//----------------------------------------------------------------------------------------------

	private static RestClient UseNewClient() =>
		new(c => c.BaseUrl = new Uri("http://localhost:5222"));

	//----------------------------------------------------------------------------------------------

	private static RestClient? _shared;

	private static RestClient UseSharedClient() =>
		_shared ??= UseNewClient();

	//----------------------------------------------------------------------------------------------

	private static RestClient UseTheirPooledClient() =>
		new(c => c.BaseUrl = new Uri("http://localhost:5222"), null, null, true);

	//----------------------------------------------------------------------------------------------

	private static RestClient UseOursPooledClient() =>
		PooledRestClient.Acquire(c => c.BaseUrl = new Uri("http://localhost:5222"));
}
