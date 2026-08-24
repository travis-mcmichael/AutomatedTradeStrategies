using System.Text;

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

// Proxies the add/update ModelConfig call so the browser only ever talks to this origin.
app.MapPost("/api/models", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    var client = httpClientFactory.CreateClient("Api");
    var response = await client.PostAsync("/models", new StringContent(body, Encoding.UTF8, "application/json"));
    var json = await response.Content.ReadAsStringAsync();
    return Results.Content(json, "application/json", null, (int)response.StatusCode);
});

app.Run();
