namespace AutomatedTrading.Console.ApiClient;

public class StrategyApiClient : IDisposable
{
    private readonly HttpClient _http;

    public StrategyApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public void Dispose() => _http.Dispose();
}
