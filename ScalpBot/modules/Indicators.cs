using System;
using System.Collections.Generic;
using System.Linq;

namespace ScalpBot.modules;

public static class Indicators
{
    public static decimal EMA(List<decimal> prices, int period)
    {
        if (prices == null || prices.Count == 0) return 0;
        if (prices.Count <= period) return prices.Last();
        decimal k = 2m / (period + 1);
        decimal ema = prices.Take(period).Average();
        for (int i = period; i < prices.Count; i++)
            ema = prices[i] * k + ema * (1 - k);
        return ema;
    }

    public static decimal RSI(List<decimal> closes, int period = 14)
    {
        if (closes == null || closes.Count <= period) return 50m;
        var gains = new List<decimal>();
        var losses = new List<decimal>();
        for (int i = 1; i < closes.Count; i++)
        {
            var d = closes[i] - closes[i - 1];
            if (d >= 0) gains.Add(d); else losses.Add(-d);
        }
        decimal avgGain = gains.TakeLast(period).DefaultIfEmpty(0).Average();
        decimal avgLoss = losses.TakeLast(period).DefaultIfEmpty(0).Average();
        if (avgLoss == 0) return 100m;
        decimal rs = avgGain / avgLoss;
        return 100m - (100m / (1 + rs));
    }

    public static (decimal macd, decimal signal, decimal hist) MACD(List<decimal> closes)
    {
        if (closes == null || closes.Count == 0) return (0, 0, 0);
        var macdSeries = new List<decimal>();
        for (int i = 0; i < closes.Count; i++)
        {
            var window = closes.Take(i + 1).ToList();
            decimal e12 = EMA(window, Math.Min(12, window.Count));
            decimal e26 = EMA(window, Math.Min(26, window.Count));
            macdSeries.Add(e12 - e26);
        }
        var macd = macdSeries.Last();
        var signal = EMA(macdSeries, Math.Min(9, macdSeries.Count));
        return (macd, signal, macd - signal);
    }

    public static decimal ATR(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period = 14)
    {
        if (highs == null || lows == null || closes == null) return 0;
        var trs = new List<decimal>();
        for (int i = 1; i < closes.Count; i++)
        {
            var high = highs[i];
            var low = lows[i];
            var prev = closes[i - 1];
            var tr = Math.Max((double)(high - low), Math.Max((double)Math.Abs(high - prev), (double)Math.Abs(low - prev)));
            trs.Add((decimal)tr);
        }
        if (trs.Count == 0) return 0m;
        return trs.TakeLast(Math.Min(period, trs.Count)).Average();
    }

    public static decimal VWAP(List<(decimal high, decimal low, decimal close, decimal vol)> klines)
    {
        if (klines == null || klines.Count == 0) return 0;
        decimal pv = 0m, v = 0m;
        foreach (var k in klines)
        {
            var typical = (k.high + k.low + k.close) / 3m;
            pv += typical * k.vol;
            v += k.vol;
        }
        return v == 0 ? klines.Last().close : pv / v;
    }

    public static decimal OBI(decimal bidVol, decimal askVol)
    {
        if (bidVol + askVol == 0) return 0;
        return (bidVol - askVol) / (bidVol + askVol);
    }

    // Bollinger Bands
    public static (decimal upper, decimal middle, decimal lower) BollingerBands(List<decimal> closes, int period = 20, decimal multiplier = 2m)
    {
        if (closes == null || closes.Count < period) return (0, 0, 0);
        var subset = closes.Skip(closes.Count - period).ToList();
        decimal sma = subset.Average();
        decimal variance = subset.Sum(x => (x - sma) * (x - sma)) / period;
        decimal stdDev = (decimal)Math.Sqrt((double)variance);
        decimal upper = sma + multiplier * stdDev;
        decimal lower = sma - multiplier * stdDev;
        return (upper, sma, lower);
    }
}
