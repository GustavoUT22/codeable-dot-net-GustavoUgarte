using System.Collections.Concurrent;
using CachedInventory;
using Microsoft.AspNetCore.Mvc;

public static class CachedInventoryApiBuilder
{
  public static WebApplication Build(string[] args)
  {
    var builder = WebApplication.CreateBuilder(args);
    var cache = new ConcurrentDictionary<int, int>();
    var timers = new ConcurrentDictionary<int, Timer>();
    var semaphores = new ConcurrentDictionary<int, SemaphoreSlim>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddScoped<IWarehouseStockSystemClient, WarehouseStockSystemClient>();
    builder.Services.AddSingleton(cache);
    builder.Services.AddSingleton(timers);
    builder.Services.AddSingleton(semaphores);
    builder.Services.AddScoped<StockService>();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
      app.UseSwagger();
      app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.MapGet("/stock/{productId:int}", async (
      [FromServices] StockService stockService,
      int productId) =>
    {
      var stock = await stockService.GetStockCache(productId);
      return Results.Ok(stock);
    }).WithName("GetStock").WithOpenApi();

    app.MapPost("/stock/retrieve", async (
      [FromServices] StockService stockService,
      [FromBody] RetrieveStockRequest req) =>
    {
      await stockService.RetrieveProduct(req.ProductId, req.Amount);
      return Results.Ok();
    }).WithName("RetrieveStock").WithOpenApi();

    app.MapPost("/stock/restock", async (
      [FromServices] StockService stockService,
      [FromBody] RestockRequest req) =>
    {
      await stockService.RestockProduct(req.ProductId, req.Amount);
      return Results.Ok();
    }).WithName("Restock").WithOpenApi();

    return app;
  }
}

public record RetrieveStockRequest(int ProductId, int Amount);
public record RestockRequest(int ProductId, int Amount);
