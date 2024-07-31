using System.Collections.Concurrent;
using CachedInventory;

public class StockService
{
  private readonly IWarehouseStockSystemClient client;
  private readonly ConcurrentDictionary<int, int> cache;
  private readonly ConcurrentDictionary<int, Timer> timers;
  private readonly ConcurrentDictionary<int, SemaphoreSlim> semaphores;

  public StockService(
   IWarehouseStockSystemClient client,
   ConcurrentDictionary<int, int> cache,
   ConcurrentDictionary<int, Timer> timers,
   ConcurrentDictionary<int, SemaphoreSlim> semaphores
  )
  {
    this.client = client;
    this.cache = cache;
    this.timers = timers;
    this.semaphores = semaphores;
  }
  public async Task<int> GetStockCache(int productId)
  {
    if (!cache.TryGetValue(productId, out var cachedStock))
    {
      var stock = await client.GetStock(productId);
      cache[productId] = stock;
      return stock;
    }
    return cachedStock;
  }


  public async Task RetrieveProduct(int productId, int amount)
  {
    var semaphore = semaphores.GetOrAdd(productId, new SemaphoreSlim(1, 1));
    await semaphore.WaitAsync();

    try
    {
      if (cache.TryGetValue(productId, out var cachedStock) && cachedStock >= amount)
      {
        cache[productId] = cachedStock - amount;
        ResetTimer(productId);
      }
      else
      {
        var stock = await client.GetStock(productId);
        if (stock < amount)
        {
          throw new InvalidOperationException("Not enough stock.");
        }
        cache[productId] = stock - amount;
        ResetTimer(productId);
      }
    }
    finally
    {
      semaphore.Release();
    }
  }

  public async Task RestockProduct(int productId, int amount)
  {
    var stock = await client.GetStock(productId);
    cache[productId] = amount + stock;
    ResetTimer(productId);
  }

  private void ResetTimer(int productId)
  {
    if (timers.TryGetValue(productId, out var existingTimer))
    {
      existingTimer.Change(2500, Timeout.Infinite);
    }
    else
    {
      var newTimer = new Timer(
          async state =>
          {
            if (state != null)
            {
              var pid = (int)state;
              if (cache.TryGetValue(pid, out var stock))
              {
                await client.UpdateStock(pid, stock);
              }
            }
          },
          productId,
          2500,
          Timeout.Infinite
      );
      timers[productId] = newTimer;
    }
  }
}
