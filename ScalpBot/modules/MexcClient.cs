using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Linq;

namespace ScalpBot.modules;

public class MexcClient : IDisposable
{
    private readonly HttpClient _http;
    public MexcClient()
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.mexc.com/api/v3/") };
    }

    public void Dispose() => _http?.Dispose();

    public async Task<List<List<object>>> GetKlinesAsync(string symbol = "BTCUSDT", string interval = "1m", int limit = 300)
    {
        var url = $"klines?symbol={symbol}&interval={interval}&limit={limit}";
        var text = await _http.GetStringAsync(url);
        return JArray.Parse(text).ToObject<List<List<object>>>();
    }

    public async Task<(decimal bestBid, decimal bestAsk, decimal bidVol, decimal askVol)> GetDepthSummaryAsync(string symbol, int depthLimit = 20)
    {
        var url = $"depth?symbol={symbol}&limit=50";
        var text = await _http.GetStringAsync(url);
        var jo = JObject.Parse(text);

        var bids = jo["bids"].ToObject<List<List<string>>>();
        var asks = jo["asks"].ToObject<List<List<string>>>();

        decimal bestBid = Decimal.Parse(bids[0][0], CultureInfo.InvariantCulture);
        decimal bestAsk = Decimal.Parse(asks[0][0], CultureInfo.InvariantCulture);
        decimal bidVol = 0m, askVol = 0m;

        for (int i = 0; i < Math.Min(depthLimit, bids.Count); i++)
            bidVol += Decimal.Parse(bids[i][1], CultureInfo.InvariantCulture);
        for (int i = 0; i < Math.Min(depthLimit, asks.Count); i++)
            askVol += Decimal.Parse(asks[i][1], CultureInfo.InvariantCulture);

        return (bestBid, bestAsk, bidVol, askVol);
    }

    public async Task<List<JObject>> GetTickers24hrAsync()
    {
        var text = await _http.GetStringAsync("ticker/24hr");
        return JArray.Parse(text).Select(x => (JObject)x).ToList();
    }
}
