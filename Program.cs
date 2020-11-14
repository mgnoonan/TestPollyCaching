using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.Caching;
using Polly.Caching.Memory;
using Polly.Contrib.DuplicateRequestCollapser;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SystemConsole.Themes;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .MinimumLevel.Information()
    .WriteTo.Console(theme: AnsiConsoleTheme.Code)
    .WriteTo.File(new RenderedCompactJsonFormatter(), "log.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10)
    .WriteTo.Seq("http://localhost:5341")       // docker run --rm -it -e ACCEPT_EULA=Y -p 5341:80 datalust/seq:latest
    .CreateLogger();

// Log.Information("START: Synchronous caching test");
// Parallel.For(1, 1000, (i, state) =>
// {
//     Log.Information("START: Iteration #{i}", i);
//     var result = BPO.Current.GetCachedValue(i);
//     Log.Information("RESULT: {result}", result);
// });

Log.Information("START: Asynchronous caching test");
Parallel.For(1, 1000, async (i, state) =>
{
    Log.Information("START: Iteration #{i}", i);
    var result = await BPO.Current.GetCachedValueAsync(i);
    Log.Information("RESULT: Iteration #{i}:{result}", i, result);
});

Log.Information("END");
Log.CloseAndFlush();

public class BPO
{
    private CachePolicy cachePolicy;
    private ISyncRequestCollapserPolicy collapserPolicy;
    private AsyncCachePolicy cachePolicyAsync;
    private IAsyncRequestCollapserPolicy collaperPolicyAsync;
    private static BPO _current;
    private static object _syncRoot = new object();

    /// <summary>
    /// Holds handle to static instance of self.  This allows us to use inheritance while only having to instantiate one object.
    /// </summary>
    public static BPO Current
    {
        get
        {
            if (_current == null)
            {
                lock (_syncRoot)
                {
                    if (_current == null)
                    {
                        _current = new BPO();
                    }
                }
            }

            return _current;
        }
    }

    public BPO()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var memoryCacheProvider = new MemoryCacheProvider(memoryCache);

        cachePolicy = Policy.Cache(memoryCacheProvider, TimeSpan.FromSeconds(2));
        collapserPolicy = RequestCollapserPolicy.Create();

        cachePolicyAsync = Policy.CacheAsync(memoryCacheProvider, TimeSpan.FromSeconds(2));
        collaperPolicyAsync = AsyncRequestCollapserPolicy.Create();
    }

    public string GetCachedValue(int iteration)
    {
        return cachePolicy.Wrap(collapserPolicy).Execute(
            context => FetchValueFromResource(iteration), new Context("SYNC_KEY"));
    }

    private string FetchValueFromResource(int iteration)
    {
        Log.Information("START: FetchValueFromResource from iteration {iteration}", iteration);
        string result = $"Cached by iteration #{iteration}";
        Thread.Sleep(2500);
        Log.Information("END: FetchValueFromResource");
        Log.Information(result);

        return result;
    }

    public async Task<string> GetCachedValueAsync(int iteration)
    {
        return await cachePolicyAsync.WrapAsync(collaperPolicyAsync).ExecuteAsync(
            async (context) => await FetchValueFromResourceAsync(iteration), new Context("ASYNC_KEY"));
    }

    private async Task<string> FetchValueFromResourceAsync(int iteration)
    {
        Log.Information("START: FetchValueFromResourceAsync from iteration {iteration}", iteration);
        string result = await Task.FromResult<string>($"Cached by iteration #{iteration}");
        Thread.Sleep(2500);
        Log.Information("END: FetchValueFromResourceAsync");
        Log.Information(result);

        return result;
    }

}
