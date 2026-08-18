var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddHttpClient("Api", client =>
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5244";
    client.BaseAddress = new Uri(apiBaseUrl);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseDefaultFiles();
app.UseStaticFiles();

// Proxies AutomatedTrading.Core.Api's /models so the browser only ever talks to this origin.
app.MapGet("/api/models", async (IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient("Api");
    var response = await client.GetAsync("/models");
    var json = await response.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", null, (int)response.StatusCode);
});

app.Run();
