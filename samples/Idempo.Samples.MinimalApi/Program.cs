using System.Collections.Concurrent;
using Idempo.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIdempo();
var app = builder.Build();

app.UseRouting();
app.UseIdempo();

var orders = new ConcurrentDictionary<int, string>();
var nextOrderId = 0;

app.MapPost("/orders", (OrderRequest request) =>
{
    var id = Interlocked.Increment(ref nextOrderId);
    orders[id] = request.Sku;
    return Results.Created($"/orders/{id}", new { orderId = id, sku = request.Sku, totalOrders = orders.Count });
});

app.MapGet("/orders", () => orders);

app.Run();

record OrderRequest(string Sku);
