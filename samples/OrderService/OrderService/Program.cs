using OrderService.Domain;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var customers = new List<Customer>
{
    new(1, "Ada Example", new Address("1 Test Street", "Springfield", "12345", "us")),
    new(2, "Pickup Customer", null),
};
var orders = Enumerable.Range(1, 23).Select(i => new Order { Id = i, Customer = customers[i % 2] }).ToList();
foreach (var o in orders) o.Items.Add(new LineItem("SKU-" + o.Id, 9.99m, 1 + o.Id % 3));

Order? Find(int id) => orders.FirstOrDefault(o => o.Id == id);

app.MapGet("/orders", (int page = 1, int size = 10) =>
    Paging.Paginate(orders.Select(o => new { o.Id, o.Customer.Name }).ToList(), page, size));

app.MapGet("/orders/{id:int}/invoice", (int id) => Find(id) is { } o
    ? Results.Ok(new { o.Id, Subtotal = Pricing.Subtotal(o), Total = Pricing.InvoiceTotal(o) })
    : Results.NotFound());

app.MapGet("/orders/{id:int}/label", (int id) => Find(id) is { } o
    ? Results.Text(Shipping.Label(o.Customer))
    : Results.NotFound());

app.MapPost("/orders/{id:int}/items", (int id, string sku, decimal price, string qty) =>
{
    if (Find(id) is not { } o) return Results.NotFound();
    o.Items.Add(new LineItem(sku, price, QuantityParser.Parse(qty)));
    return Results.Ok(new { o.Id, Total = Pricing.InvoiceTotal(o) });
});

app.MapPost("/orders/{id:int}/discounts", (int id, Discount d) =>
{
    if (Find(id) is not { } o) return Results.NotFound();
    o.Discounts.Add(d);
    return Results.Ok(new { o.Id, Total = Pricing.InvoiceTotal(o) });
});

app.Run();
