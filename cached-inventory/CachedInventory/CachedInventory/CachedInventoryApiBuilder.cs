using System.Collections.Concurrent;
using System.Diagnostics;
using CachedInventory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

public static class CachedInventoryApiBuilder
{
  public static WebApplication Build(string[] args)
  {
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddScoped<IWarehouseStockSystemClient, WarehouseStockSystemClient>();
    builder.Services.AddMemoryCache();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
      app.UseSwagger();
      app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    var cache = app.Services.GetRequiredService<IMemoryCache>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var stockLocks = new ConcurrentDictionary<int, SemaphoreSlim>();

    async Task<int> GetStockCache(int productId, IWarehouseStockSystemClient client)
    {
      if (!cache.TryGetValue(productId, out int cachedStock))
      {
        logger.LogInformation("Cache miss for productId: {ProductId}", productId);
        cachedStock = await client.GetStock(productId);

        cache.Set(productId, cachedStock, TimeSpan.FromMinutes(10));

        logger.LogInformation("Stock fetched and cached for productId: {ProductId}", productId);
      }
      else
      {
        logger.LogInformation("Cache hit for productId: {ProductId}", productId);
      }
      return cachedStock;
    }

    static void UpdateCache(IMemoryCache cache, int productId, int stock)
    {
      var cacheEntryOptions = new MemoryCacheEntryOptions
      {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
      };
      cache.Set(productId, stock, cacheEntryOptions);
    }

    app.MapGet("/stock/{productId:int}", async ([FromServices] IWarehouseStockSystemClient client, int productId) =>
    {
      var stopwatch = Stopwatch.StartNew();
      var stock = await GetStockCache(productId, client);
      logger.LogInformation("Time elapsed: {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
      return Results.Ok(stock);
    }).WithName("GetStock").WithOpenApi();

    app.MapPost(
      "/stock/retrieve",
      async ([FromServices] IWarehouseStockSystemClient client, [FromBody] RetrieveStockRequest req) =>
      {
        if (req.Amount <= 0)
        {
          return Results.BadRequest("Amount must be greater than zero.");
        }

        var semaphore = stockLocks.GetOrAdd(req.ProductId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
          var stock = await GetStockCache(req.ProductId, client);
          if (stock < req.Amount)
          {
            return Results.BadRequest("Not enough stock.");
          }

          await client.UpdateStock(req.ProductId, stock - req.Amount);
          UpdateCache(cache, req.ProductId, stock - req.Amount);

          return Results.Ok();
        }
        finally
        {
          semaphore.Release();
        }
      })
  .WithName("RetrieveStock")
  .WithOpenApi();

    app.MapPost(
        "/stock/restock",
        async ([FromServices] IWarehouseStockSystemClient client, [FromBody] RestockRequest req) =>
        {
          if (req.Amount <= 0)
          {
            return Results.BadRequest("Amount must be greater than zero.");
          }

          var stock = await GetStockCache(req.ProductId, client);
          await client.UpdateStock(req.ProductId, req.Amount + stock);
          UpdateCache(cache, req.ProductId, req.Amount + stock);

          return Results.Ok();
        })
    .WithName("Restock")
    .WithOpenApi();
    return app;
  }
}

public record RetrieveStockRequest(int ProductId, int Amount);
public record RestockRequest(int ProductId, int Amount);
