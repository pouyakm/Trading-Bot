namespace ScalpBot.modules;

public class Analyzer
{
    public string Decide(
        int emaDir, decimal rsi, decimal obi, decimal price,
        decimal vwap, decimal macdHist, decimal atr,
        decimal probUp, decimal bbUpper, decimal bbLower, bool psarTrendUp)
    {
        // normalize all values to -1..+1
        decimal emaScore = emaDir; // -1 or +1
        decimal rsiScore = (rsi - 50m) / 50m; // RSI 0-100 → -1..+1
        decimal macdScore = Math.Clamp(macdHist / 0.001m, -1m, 1m); // normalize macd hist
        decimal vwapScore = price > vwap ? 0.5m : -0.5m;
        decimal atrScore = Math.Clamp(atr / price, -1m, 1m);
        decimal obiScore = Math.Clamp(obi, -1m, 1m);
        decimal bbScore = price > bbUpper ? -0.7m : price < bbLower ? 0.7m : 0m;
        decimal lstmScore = (probUp - 0.5m) * 2m; // 0.5 → 0, 1 → +1, 0 → -1
        decimal psarScore = psarTrendUp ? 0.5m : -0.5m;
        decimal wPSAR = 0.10m;
        
        // weights
        decimal wEMA = 0.25m;
        decimal wRSI = 0.15m;
        decimal wMACD = 0.15m;
        decimal wVWAP = 0.10m;
        decimal wATR = 0.05m;
        decimal wOBI = 0.05m;
        decimal wBB = 0.10m;
        decimal wLSTM = 0.15m;

        // weighted score
        decimal score =
            emaScore * wEMA +
            rsiScore * wRSI +
            macdScore * wMACD +
            vwapScore * wVWAP +
            atrScore * wATR +
            obiScore * wOBI +
            bbScore * wBB +
            lstmScore * wLSTM +
            psarScore * wPSAR;


        // thresholds
        decimal buyThreshold = 0.15m;
        decimal sellThreshold = -0.15m;

        if (score >= buyThreshold)
            return "BUY";
        else if (score <= sellThreshold)
            return "SELL";
        else
            return "HOLD";
    }
}
