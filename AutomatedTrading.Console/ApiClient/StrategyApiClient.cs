using System.Net.Http.Json;
using AutomatedTrading.Core.Api.Models;

namespace AutomatedTrading.Console.ApiClient;

public class StrategyApiClient : IDisposable
{
    private readonly HttpClient _http;

    public StrategyApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<StrategySessionResponse?> StartSessionAsync(StrategySessionRequest req) =>
        await postAsync<StrategySessionRequest, StrategySessionResponse>("/strategy/session", req);

    public async Task<StrategyBarResponse?> EvaluateBarAsync(StrategyBarRequest req) =>
        await postAsync<StrategyBarRequest, StrategyBarResponse>("/strategy/bar", req);

    private async Task<TResponse?> postAsync<TRequest, TResponse>(string path, TRequest req)
        where TResponse : class
    {
        try
        {
            var response = await _http.PostAsJsonAsync(path, req);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
