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

    // ── Session High ──────────────────────────────────────────────────────────

    public async Task<SessionHighSessionResponse?> StartSessionHighAsync(SessionHighSessionRequest req)
    {
        var resp = await _http.PostAsJsonAsync("/strategy/session-high/session", req);
        return await resp.Content.ReadFromJsonAsync<SessionHighSessionResponse>();
    }

    public async Task<SessionHighBarResponse?> EvaluateSessionHighBarAsync(SessionHighBarRequest req)
    {
        var resp = await _http.PostAsJsonAsync("/strategy/session-high/bar", req);
        return await resp.Content.ReadFromJsonAsync<SessionHighBarResponse>();
    }

    // ── Session Low ───────────────────────────────────────────────────────────

    public async Task<SessionLowSessionResponse?> StartSessionLowAsync(SessionLowSessionRequest req)
    {
        var resp = await _http.PostAsJsonAsync("/strategy/session-low/session", req);
        return await resp.Content.ReadFromJsonAsync<SessionLowSessionResponse>();
    }

    public async Task<SessionLowBarResponse?> EvaluateSessionLowBarAsync(SessionLowBarRequest req)
    {
        var resp = await _http.PostAsJsonAsync("/strategy/session-low/bar", req);
        return await resp.Content.ReadFromJsonAsync<SessionLowBarResponse>();
    }

    // ── Trade Day High ────────────────────────────────────────────────────────

    public async Task<TradeDayHighSessionResponse?> StartTradeDayHighAsync(TradeDayHighSessionRequest req)
    {
        var resp = await _http.PostAsJsonAsync("/strategy/trade-day-high/session", req);
        return await resp.Content.ReadFromJsonAsync<TradeDayHighSessionResponse>();
    }

    public async Task<TradeDayHighBarResponse?> EvaluateTradeDayHighBarAsync(TradeDayHighBarRequest req)
    {
        var resp = await _http.PostAsJsonAsync("/strategy/trade-day-high/bar", req);
        return await resp.Content.ReadFromJsonAsync<TradeDayHighBarResponse>();
    }

    // ── Trade Day Low ─────────────────────────────────────────────────────────

    public async Task<TradeDayLowSessionResponse?> StartTradeDayLowAsync(TradeDayLowSessionRequest req)
    {
        var resp = await _http.PostAsJsonAsync("/strategy/trade-day-low/session", req);
        return await resp.Content.ReadFromJsonAsync<TradeDayLowSessionResponse>();
    }

    public async Task<TradeDayLowBarResponse?> EvaluateTradeDayLowBarAsync(TradeDayLowBarRequest req)
    {
        var resp = await _http.PostAsJsonAsync("/strategy/trade-day-low/bar", req);
        return await resp.Content.ReadFromJsonAsync<TradeDayLowBarResponse>();
    }

    public void Dispose() => _http.Dispose();
}
