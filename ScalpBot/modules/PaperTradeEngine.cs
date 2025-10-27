using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ScalpBot.modules;

public enum Side { Long, Short }

public class PaperTrade
{
    public string Symbol { get; set; }
    public Side Side { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Qty { get; set; }
    public decimal StopPrice { get; set; }
    public decimal TakeProfit { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime? ExitTime { get; set; }
    public decimal Pnl => (ExitPrice - EntryPrice) * (Side == Side.Long ? 1 : -1) * Qty;
    public bool Closed => ExitTime.HasValue;
}

public class PaperTradeEngine
{
    private readonly List<PaperTrade> _trades = new();
    private readonly string _logFile;

    public decimal AccountBalance { get; private set; } // USDT
    public decimal InitialBalance { get; private set; }

    public PaperTradeEngine(decimal initialBalance = 1000m, string logFile = "paper_trades.csv")
    {
        InitialBalance = initialBalance;
        AccountBalance = initialBalance;
        _logFile = logFile;
        // write header if not exists
        if (!File.Exists(_logFile))
        {
            File.WriteAllText(_logFile, "symbol,side,entryPrice,qty,stopPrice,takeProfit,entryTime,exitPrice,exitTime,pnl,balance\n");
        }
    }

    // محاسبه qty بر اساس usdPerTrade یا درصد ریسک و ATR
    // روش اول: مبلغ ثابت (usdPerTrade) با لوریج
    public decimal CalculateQty_ByNotional(decimal usdPerTrade, decimal leverage, decimal entryPrice, int precision = 6)
    {
        decimal notional = usdPerTrade * leverage;
        decimal qty = notional / entryPrice;
        // round down
        decimal factor = (decimal)Math.Pow(10, precision);
        qty = Math.Floor(qty * factor) / factor;
        return qty;
    }

    // روش دوم: محاسبه بر اساس درصد ریسک و فاصله SL (ATR-based)
    public decimal CalculateQty_ByRiskPercent(decimal accountBalance, decimal riskPercent, decimal entryPrice, decimal slDistance, decimal leverage, int precision = 6)
    {
        decimal riskUsd = accountBalance * riskPercent; // مثلا 0.005 = 0.5%
        // مقدار دلار که می‌پذیریم از دست بدهیم برابر riskUsd
        // در قرارداد linear: pnl at stop = slDistance * qty
        // پس qty = riskUsd / slDistance
        if (slDistance <= 0) return 0;
        decimal qty = riskUsd / slDistance;
        // اگر leverage داریم، معمولا riskUsd باید بر اساس margin باشد؛ اما برای سادگی همین استفاده می‌کنیم.
        decimal factor = (decimal)Math.Pow(10, precision);
        qty = Math.Floor(qty * factor) / factor;
        return qty;
    }

    // باز کردن پوزیشن شبیه‌سازی (entry)
    public PaperTrade OpenPosition(string symbol, Side side, decimal entryPrice, decimal qty, decimal stopPrice, decimal takeProfit)
    {
        var t = new PaperTrade
        {
            Symbol = symbol,
            Side = side,
            EntryPrice = entryPrice,
            Qty = qty,
            StopPrice = stopPrice,
            TakeProfit = takeProfit,
            EntryTime = DateTime.UtcNow
        };
        _trades.Add(t);
        return t;
    }

    // بستن پوزیشن شبیه‌سازی (exit)
    public void ClosePosition(PaperTrade trade, decimal exitPrice)
    {
        if (trade == null || trade.Closed) return;
        trade.ExitPrice = exitPrice;
        trade.ExitTime = DateTime.UtcNow;
        // به روز رسانی بالانس
        AccountBalance += trade.Pnl;
        // لاگ
        LogTrade(trade);
    }

    private void LogTrade(PaperTrade t)
    {
        var line = string.Format(CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}\n",
            t.Symbol,
            t.Side,
            t.EntryPrice,
            t.Qty,
            t.StopPrice,
            t.TakeProfit,
            t.EntryTime.ToString("o"),
            (t.Closed ? t.ExitPrice.ToString(CultureInfo.InvariantCulture) : ""),
            (t.ExitTime.HasValue ? t.ExitTime.Value.ToString("o") : ""),
            t.Pnl,
            AccountBalance
        );
        File.AppendAllText(_logFile, line);
    }

    // انتخاب و بستن پوزیشن‌ها با قیمت بازار شبیه‌سازی (مثلاً در هر تیک قیمت بررسی می‌کنیم)
    public void CheckAndCloseByPrice(decimal currentPrice)
    {
        var openTrades = _trades.Where(x => !x.Closed).ToList();
        foreach (var t in openTrades)
        {
            if (t.Side == Side.Long)
            {
                if (currentPrice <= t.StopPrice || currentPrice >= t.TakeProfit)
                    ClosePosition(t, currentPrice);
            }
            else
            {
                if (currentPrice >= t.StopPrice || currentPrice <= t.TakeProfit)
                    ClosePosition(t, currentPrice);
            }
        }
    }

    // گزارش خلاصه
    public void PrintReport()
    {
        var closed = _trades.Where(x => x.Closed).ToList();
        var wins = closed.Where(x => x.Pnl > 0).ToList();
        var losses = closed.Where(x => x.Pnl <= 0).ToList();
        decimal totalPnl = closed.Sum(x => x.Pnl);
        decimal winRate = closed.Count == 0 ? 0 : (decimal)wins.Count / closed.Count;
        decimal maxDrawdown = CalculateMaxDrawdown();

        Console.WriteLine("=== PAPER TRADING REPORT ===");
        Console.WriteLine($"Initial Balance: {InitialBalance}");
        Console.WriteLine($"Final Balance: {AccountBalance}");
        Console.WriteLine($"Total Trades: {closed.Count}");
        Console.WriteLine($"Wins: {wins.Count} | Losses: {losses.Count} | WinRate: {winRate:P2}");
        Console.WriteLine($"Total PnL: {totalPnl:F6}");
        Console.WriteLine($"Max Drawdown (approx): {maxDrawdown:P2}");
        Console.WriteLine("============================");
    }

    // محاسبه تقریبی Max Drawdown از بالانس لحظه‌ای
    private decimal CalculateMaxDrawdown()
    {
        var lines = File.ReadAllLines(_logFile).Skip(1).ToArray();
        var balances = new List<decimal>() { InitialBalance };
        foreach (var ln in lines)
        {
            var parts = ln.Split(',');
            if (parts.Length > 10)
            {
                if (decimal.TryParse(parts[10], NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    balances.Add(b);
            }
        }
        decimal peak = balances[0];
        decimal maxDd = 0m;
        foreach (var b in balances)
        {
            if (b > peak) peak = b;
            var dd = (peak - b) / peak;
            if (dd > maxDd) maxDd = dd;
        }
        return maxDd;
    }

    // دسترسی به همه معاملات (برای پردازش بیشتر)
    public IReadOnlyList<PaperTrade> GetAllTrades() => _trades.AsReadOnly();
}
