using System.Text;

public static class Endpoints
{
    public static void Map(WebApplication app)
    {
        _ = app.MapGet("api/models", async (IHttpClientFactory httpClientFactory) =>
        {
            var client = httpClientFactory.CreateClient("Api");
            var response = await client.GetAsync("/models");
            var json = await response.Content.ReadAsStringAsync();
            return Results.Content(json, "application/json", null, (int)response.StatusCode);
        });

        _ = app.MapGet("api/features", async (IHttpClientFactory httpClientFactory) =>
        {
            var client = httpClientFactory.CreateClient("Api");
            var response = await client.GetAsync("/features");
            var json = await response.Content.ReadAsStringAsync();
            return Results.Content(json, "application/json", null, (int)response.StatusCode);
        });

        app.MapPost("api/models", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(;
            var client = httpClientFactory.CreateClient("Api");
            var response = await client.PostAsync("/models", new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();
            return Results.Content(json, "application/json", null, (int)response.StatusCode);
        });
    }
}