using Newtonsoft.Json.Linq;
using ScalpBot.modules; // برای PaperTradeEngine
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ScalpBot.backtest;

public class Backtester
{
    private readonly PaperTradeEngine _paper;
    private readonly decimal _riskPercent;
    private readonly decimal _slMultiplier;
    private readonly decimal _rewardRatio;
    private readonly decimal _leverage;

    public Backtester(decimal initialBalance = 1000m, decimal riskPercent = 0.005m, decimal slMultiplier = 1.5m, decimal rewardRatio = 2m, decimal leverage = 5m)
    {
        _paper = new PaperTradeEngine(initialBalance);
        _riskPercent = riskPercent;
        _slMultiplier = slMultiplier;
        _rewardRatio = rewardRatio;
        _leverage = leverage;
    }

    public async Task RunAsync(string symbol, string interval = "1m", int limit = 1000)
    {
        Console.WriteLine($"🔹 شروع بک‌تست برای {symbol} با {limit} کندل...");

        var candles = await FetchHistoricalData(symbol, interval, limit);
        if (candles.Count < 50)
        {
            Console.WriteLine("داده کافی نیست!");
            return;
        }

        // جدا کردن داده‌ها
        var closes = candles.Select(x => x.close).ToList();
        var highs = candles.Select(x => x.high).ToList();
        var lows = candles.Select(x => x.low).ToList();

        for (int i = 50; i < candles.Count; i++)
        {
            var closeSlice = closes.Take(i).ToList();
            var highSlice = highs.Take(i).ToList();
            var lowSlice = lows.Take(i).ToList();

            // محاسبه اندیکاتورها
            decimal emaFast = EMA(closeSlice, 9);
            decimal emaSlow = EMA(closeSlice, 21);
            decimal rsi = RSI(closeSlice, 14);
            decimal atr = ATR(highSlice, lowSlice, closeSlice, 14);
            decimal macd = MACD(closeSlice).Item1;
            decimal signal = MACD(closeSlice).Item2;
            decimal vwap = VWAP(highSlice, lowSlice, closeSlice);
            var bb = Bollinger(closeSlice, 20, 2);
            decimal obi = OBI(closeSlice);

            // تصمیم ساده بر اساس ترکیب چند اندیکاتور
            string decision = "HOLD";
            if (emaFast > emaSlow && rsi < 70 && macd > signal && closeSlice.Last() > bb.upper && obi > 0)
                decision = "BUY";
            else if (emaFast < emaSlow && rsi > 30 && macd < signal && closeSlice.Last() < bb.lower && obi < 0)
                decision = "SELL";

            decimal entryPrice = closeSlice.Last();
            decimal slDistance = atr * _slMultiplier;
            if (slDistance <= 0) slDistance = entryPrice * 0.002m;

            if (decision == "BUY" || decision == "SELL")
            {
                Side side = decision == "BUY" ? Side.Long : Side.Short;
                decimal qty = _paper.CalculateQty_ByRiskPercent(_paper.AccountBalance, _riskPercent, entryPrice, slDistance, _leverage);
                decimal stopPrice = side == Side.Long ? entryPrice - slDistance : entryPrice + slDistance;
                decimal tpPrice = side == Side.Long ? entryPrice + (slDistance * _rewardRatio) : entryPrice - (slDistance * _rewardRatio);

                _paper.OpenPosition(symbol, side, entryPrice, qty, stopPrice, tpPrice);
            }

            // بررسی بسته‌شدن پوزیشن‌ها
            _paper.CheckAndCloseByPrice(closeSlice.Last());
        }

        _paper.PrintReport();
    }

    // گرفتن داده تاریخی از MEXC
    private async Task<List<(decimal open, decimal high, decimal low, decimal close, decimal volume)>> FetchHistoricalData(string symbol, string interval, int limit)
    {
        using var client = new HttpClient();
        string url = $"https://api.mexc.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        var json = JArray.Parse(await client.GetStringAsync(url));
        var list = new List<(decimal, decimal, decimal, decimal, decimal)>();
        foreach (var k in json)
        {
            list.Add((
                decimal.Parse(k[1].ToString(), CultureInfo.InvariantCulture),
                decimal.Parse(k[2].ToString(), CultureInfo.InvariantCulture),
                decimal.Parse(k[3].ToString(), CultureInfo.InvariantCulture),
                decimal.Parse(k[4].ToString(), CultureInfo.InvariantCulture),
                decimal.Parse(k[5].ToString(), CultureInfo.InvariantCulture)
            ));
        }
        return list;
    }

    // --- اندیکاتورهای ساده ---
    private decimal EMA(List<decimal> values, int period)
    {
        decimal k = 2m / (period + 1);
        decimal ema = values.Take(period).Average();
        for (int i = period; i < values.Count; i++)
            ema = values[i] * k + ema * (1 - k);
        return ema;
    }

    private decimal RSI(List<decimal> values, int period)
    {
        if (values.Count < period + 1) return 50;
        decimal gain = 0, loss = 0;
        for (int i = values.Count - period; i < values.Count; i++)
        {
            decimal diff = values[i] - values[i - 1];
            if (diff >= 0) gain += diff; else loss -= diff;
        }
        if (loss == 0) return 100;
        decimal rs = gain / loss;
        return 100 - (100 / (1 + rs));
    }

    private decimal ATR(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
    {
        if (highs.Count < period) return 0;
        var trs = new List<decimal>();
        for (int i = 1; i < highs.Count; i++)
        {
            decimal tr = Math.Max(highs[i] - lows[i], Math.Max(Math.Abs(highs[i] - closes[i - 1]), Math.Abs(lows[i] - closes[i - 1])));
            trs.Add(tr);
        }
        return trs.Skip(trs.Count - period).Average();
    }

    private (decimal, decimal) MACD(List<decimal> values)
    {
        decimal ema12 = EMA(values, 12);
        decimal ema26 = EMA(values, 26);
        decimal macd = ema12 - ema26;
        var macdList = values.Select((v, i) => ema12 - ema26).ToList();
        decimal signal = EMA(macdList, 9);
        return (macd, signal);
    }

    private decimal VWAP(List<decimal> highs, List<decimal> lows, List<decimal> closes)
    {
        int n = closes.Count;
        decimal tpSum = 0, volSum = 0;
        for (int i = 0; i < n; i++)
        {
            decimal typical = (highs[i] + lows[i] + closes[i]) / 3m;
            decimal volume = 1; // اگر داده حجم داری، اینجا بگذار
            tpSum += typical * volume;
            volSum += volume;
        }
        return tpSum / volSum;
    }

    private (decimal upper, decimal middle, decimal lower) Bollinger(List<decimal> closes, int period, decimal mult)
    {
        if (closes.Count < period) return (0, 0, 0);
        var slice = closes.Skip(closes.Count - period).ToList();
        decimal mean = slice.Average();
        decimal sd = (decimal)Math.Sqrt(slice.Sum(x => Math.Pow((double)(x - mean), 2)) / period);
        return (mean + mult * sd, mean, mean - mult * sd);
    }

    private decimal OBI(List<decimal> closes)
    {
        if (closes.Count < 2) return 0;
        decimal obi = 0;
        for (int i = 1; i < closes.Count; i++)
            obi += closes[i] > closes[i - 1] ? 1 : -1;
        return obi;
    }
}
