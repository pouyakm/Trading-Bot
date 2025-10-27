namespace ScalpBot.modules;

public class Analyzer
{
    public int Wema = 2;
    public int Wrsi = 1;
    public int Wobi = 2;
    public int Wvwap = 1;
    public int Wmacd = 1;
    public int Watr = 1;
    public int Wbb = 1;
    public int Wml = 2;

    public decimal MLP_buy = 0.6m;
    public decimal MLP_sell = 0.4m;
    public decimal OBI_threshold = 0.08m;
    public decimal RSI_upper = 65m;
    public decimal RSI_lower = 35m;

    public string Decide(int emaDir, decimal rsi, decimal obi, decimal price, decimal vwap, decimal macdDiff, decimal atr, decimal probUp, decimal bbUpper, decimal bbLower)
    {
        int score = 0;
        score += emaDir * Wema;
        if (rsi < RSI_upper) score += Wrsi; else score -= Wrsi;
        if (obi > OBI_threshold) score += Wobi; else if (obi < -OBI_threshold) score -= Wobi;
        score += (price > vwap) ? (int)Wvwap : -(int)Wvwap;
        score += (macdDiff > 0) ? Wmacd : -Wmacd;
        if (atr > 0 && atr / price > 0.01m) score = (int)System.Math.Round(score * 0.6);
        if (probUp > MLP_buy) score += Wml; else if (probUp < MLP_sell) score -= Wml;
        if (price < bbLower) score += Wbb; else if (price > bbUpper) score -= Wbb;
        if (score >= 4) return "BUY";
        if (score <= -4) return "SELL";
        return "HOLD";
    }
}
