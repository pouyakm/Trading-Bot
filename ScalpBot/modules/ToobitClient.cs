using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ToobitApi;

public class ToobitClient
{
    private readonly string _apiKey;
    private readonly string _secretKey;
    private readonly HttpClient _http;

    private const string BASE_WSS_URL = "wss://stream.toobit.com";
    private const string BASE_URL = "https://api.toobit.com";

    public ToobitClient(string apiKey, string secretKey)
    {
        _apiKey = apiKey;
        _secretKey = secretKey;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("X-BB-APIKEY", _apiKey);
    }

    //──────────────────────────────
    // 🔹 Public endpoints
    //──────────────────────────────

    public async Task<string> GetTickerAsync(string symbol)
    {
        var url = $"{BASE_URL}/quote/v1/contract/ticker/24hr";
        if (!string.IsNullOrEmpty(symbol)) url += $"?symbol ={symbol}";
        return await _http.GetStringAsync(url);
    }

    public async Task<string> GetOrderBookAsync(string symbol, int limit = 20)
    {
        var url = $"{BASE_URL}/quote/v1/depth?symbol={symbol}&limit={limit}";
        return await _http.GetStringAsync(url);
    }

    public async Task<string> GetKlinesAsync(string symbol, string interval = "1m", int limit = 100)
    {
        var url = $"{BASE_URL}/quote/v1/klines?symbol={symbol}&interval={interval}&limit={limit}";
        return await _http.GetStringAsync(url);
    }

    //──────────────────────────────
    // 🔹 Private endpoints (signed)
    //──────────────────────────────

    private string Sign(string query)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(query));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    private string BuildQuery(Dictionary<string, object> parameters)
    {
        var sb = new StringBuilder();
        foreach (var kv in parameters)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append($"{kv.Key}={kv.Value}");
        }
        return sb.ToString();
    }

    //──────────────────────────────
    // 🔸 Futures: Account Info
    //──────────────────────────────
    public async Task<string> GetBalanceAsync()
    {
        var parameters = new Dictionary<string, object>
        {
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/balance?{BuildQuery(parameters)}";
        return await _http.GetStringAsync(url);
    }

    //──────────────────────────────
    // 🔸 Place Order
    //──────────────────────────────
    public async Task<string> PlaceOrderAsync(string symbol, string side, string type, decimal quantity, decimal? price = null)
    {
        var parameters = new Dictionary<string, object>
        {
            {"symbol", $"{symbol.Substring(0, symbol.Length - 4)}-SWAP-{symbol.Substring(symbol.Length - 4)}"},
            {"side", side},           // BUY or SELL
            {"type", type},           // MARKET or LIMIT
            {"quantity", quantity},
            {"priceType", "MARKET"},
            {"newClientOrderId", Guid.NewGuid().ToString()},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };
        if (price != null) parameters.Add("price", price);

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/order";
        var formParams = new Dictionary<string, string>();
        foreach (var kv in parameters)
            formParams[kv.Key] = kv.Value.ToString();
        var content = new FormUrlEncodedContent(formParams);
        var res = await _http.PostAsync(url, content);
        return await res.Content.ReadAsStringAsync();
    }

    //──────────────────────────────
    // 🔸 Order Status
    //──────────────────────────────
    public async Task<string> OrderStatusAsync(string orderId)
    {
        var parameters = new Dictionary<string, object>
        {
            {"orderId", orderId},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/order?{BuildQuery(parameters)}";
        return await _http.GetStringAsync(url);
    }

    //──────────────────────────────
    // 🔸 Cancel Order
    //──────────────────────────────
    public async Task<string> CancelOrderAsync(string orderId)
    {
        var parameters = new Dictionary<string, object>
        {
            {"orderId", orderId},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/order?{BuildQuery(parameters)}";
        var res = await _http.DeleteAsync(url);
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<string> CancelOrdersAsync(string symbol, string side)
    {
        var parameters = new Dictionary<string, object>
        {
            {"symbol", symbol},
            {"side", side},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/batchOrders?{BuildQuery(parameters)}";
        var res = await _http.DeleteAsync(url);
        return await res.Content.ReadAsStringAsync();
    }

    //──────────────────────────────
    // 🔸 Positions
    //──────────────────────────────

    public async Task<string> SetMarginType(string symbol, string marginType)
    {
        var parameters = new Dictionary<string, object>
        {

            {"symbol", symbol},
            {"marginType", marginType},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/marginType";
        var formParams = new Dictionary<string, string>();
        foreach (var kv in parameters)
            formParams[kv.Key] = kv.Value.ToString();
        var content = new FormUrlEncodedContent(formParams);
        var res = await _http.PostAsync(url, content);
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<string> SetLeverage(string symbol, int leverage)
    {
        var parameters = new Dictionary<string, object>
        {

            {"symbol", symbol},
            {"leverage", leverage},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/marginType";
        var formParams = new Dictionary<string, string>();
        foreach (var kv in parameters)
            formParams[kv.Key] = kv.Value.ToString();
        var content = new FormUrlEncodedContent(formParams);
        var res = await _http.PostAsync(url, content);
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<string> SetTradingStop(string symbol, string side, string tp, string sl)
    {
        var parameters = new Dictionary<string, object>
        {

            {"symbol", symbol},
            {"side", side},
            {"takeProfit", tp},
            {"stopLoss", sl},
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/position/trading-stop";
        var formParams = new Dictionary<string, string>();
        foreach (var kv in parameters)
            formParams[kv.Key] = kv.Value.ToString();
        var content = new FormUrlEncodedContent(formParams);
        var res = await _http.PostAsync(url, content);
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<string> GetPositionsAsync(string symbol = null)
    {
        var parameters = new Dictionary<string, object>
        {
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };
        if (symbol != null) parameters.Add("symbol", symbol);

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/positions?{BuildQuery(parameters)}";
        return await _http.GetStringAsync(url);
    }

    public async Task<string> GetTodayPnlAsync()
    {
        var parameters = new Dictionary<string, object>
        {
            {"timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
        };

        var query = BuildQuery(parameters);
        parameters.Add("signature", Sign(query));

        var url = $"{BASE_URL}/api/v1/futures/todayPnl?{BuildQuery(parameters)}";
        return await _http.GetStringAsync(url);
    }
}
