using ScalpBot.modules;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        Directory.CreateDirectory("LSTM_Model/data");
        var mexc = new MexcClient();
        var py = new PythonModel(pythonExe: "python", scriptPath: "LSTM_Model/train_and_predict.py");
        var analyzer = new Analyzer();
        var paper = new PaperTradeEngine(initialBalance: 1000m, logFile: "paper_trades.csv");

        // select top symbols automatically
        var tickers = await mexc.GetTickers24hrAsync();
        var candidates = tickers
            .Where(t => ((string)t["symbol"]).EndsWith("USDT"))
            .Select(t => new {
                symbol = (string)t["symbol"],
                quoteVol = decimal.TryParse((string?)t["quoteVolume"], NumberStyles.Any, CultureInfo.InvariantCulture, out var qv) ? qv : 0,
                pct = decimal.TryParse((string?)t["priceChangePercent"], NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0
            })
            .Where(x => x.quoteVol > 20000000 && Math.Abs((double)x.pct) > 0)
            .Select(x => x.symbol).Take(30).ToList();

        // filter by spread and take top10
        var topSymbols = new List<string>();
        foreach (var s in candidates)
        {
            try
            {
                var (bid, ask, bidVol, askVol) = await mexc.GetDepthSummaryAsync(s, depthLimit: 5);
                var spread = (ask - bid) / ((ask + bid) / 2m) * 100m;
                if (spread <= 0.1m) topSymbols.Add(s);
                if (topSymbols.Count >= 10) break;
            }
            catch { }
        }
        Console.WriteLine("Selected symbols: " + string.Join(", ", topSymbols));

        foreach (var symbol in topSymbols)
        {
            try
            {
                var raw = await mexc.GetKlinesAsync(symbol, "1m", 300);
                var opens = raw.Select(x => Convert.ToDecimal((string)x[1], CultureInfo.InvariantCulture)).ToList();
                var highs = raw.Select(x => Convert.ToDecimal((string)x[2], CultureInfo.InvariantCulture)).ToList();
                var lows = raw.Select(x => Convert.ToDecimal((string)x[3], CultureInfo.InvariantCulture)).ToList();
                var closes = raw.Select(x => Convert.ToDecimal((string)x[4], CultureInfo.InvariantCulture)).ToList();
                var vols = raw.Select(x => Convert.ToDecimal((string)x[5], CultureInfo.InvariantCulture)).ToList();

                var last60 = Enumerable.Range(0, 60).Select(i => new {
                    o = opens[opens.Count - 60 + i],
                    h = highs[highs.Count - 60 + i],
                    l = lows[lows.Count - 60 + i],
                    c = closes[closes.Count - 60 + i],
                    v = vols[vols.Count - 60 + i]
                }).ToList();

                var klinesForVwap = last60.Select(x => (x.h, x.l, x.c, x.v)).ToList();
                decimal emaShort = Indicators.EMA(closes, 20);
                decimal emaLong = Indicators.EMA(closes, 50);
                int emaDir = emaShort > emaLong ? 1 : -1;
                decimal rsi = Indicators.RSI(closes, 14);
                var (macd, macdSignal, macdHist) = Indicators.MACD(closes);
                decimal atr = Indicators.ATR(highs, lows, closes, 14);
                decimal vwap = Indicators.VWAP(klinesForVwap);
                var depth = await mexc.GetDepthSummaryAsync(symbol, depthLimit: 20);
                decimal obi = Indicators.OBI(depth.bidVol, depth.askVol);
                var (bbUpper, bbMid, bbLower) = Indicators.BollingerBands(closes, 20, 2m);

                // write last60 CSV
                var csvPath = Path.Combine("LSTM_Model", "data", $"{symbol}_last60.csv");
                using (var sw = new StreamWriter(csvPath))
                {
                    sw.WriteLine("open,high,low,close,volume");
                    int start = raw.Count - 60;
                    for (int i = start; i < raw.Count; i++)
                    {
                        var r = raw[i];
                        sw.WriteLine($"{r[1]},{r[2]},{r[3]},{r[4]},{r[5]}");
                    }
                }

                var modelPath = Path.Combine("LSTM_Model", $"model_{symbol}.h5");
                // auto-train if not exists
                await py.TrainIfNeededAsync(Path.Combine("LSTM_Model", "data", $"{symbol}.csv"), modelPath);
                // if historical csv for symbol not exists, we can create one by collecting more data --
                // For simplicity here we expect user to have run collect_data.py for each symbol first.

                decimal probUp = 0.5m;
                if (File.Exists(modelPath))
                    probUp = await py.PredictAsync(csvPath, modelPath);

                var decision = analyzer.Decide(emaDir, rsi, obi, closes.Last(), vwap, macd - macdSignal, atr, probUp, bbUpper, bbLower);

                Console.WriteLine($"{symbol} | price={closes.Last():F6} EMA20/50={emaShort:F4}/{emaLong:F4} RSI={rsi:F2} OBI={obi:F3} VWAP={vwap:F6} MACDdiff={(macd - macdSignal):F6} ATR={atr:F6} BB={bbLower:F6}-{bbUpper:F6} ProbUp={probUp:F2} => {decision}");

                // PAPER TRADE: open simulated position if signal
                if (decision == "BUY" || decision == "SELL")
                {
                    var side = decision == "BUY" ? Side.Long : Side.Short;
                    decimal entryPrice = closes.Last();
                    decimal slDistance = atr * 1.5m;
                    if (slDistance <= 0) slDistance = entryPrice * 0.002m;
                    decimal qty = paper.CalculateQty_ByRiskPercent(paper.AccountBalance, 0.005m, entryPrice, slDistance, 5m);
                    decimal stop = side == Side.Long ? entryPrice - slDistance : entryPrice + slDistance;
                    decimal tp = side == Side.Long ? entryPrice + slDistance * 2m : entryPrice - slDistance * 2m;
                    var trade = paper.OpenPosition(symbol, side, entryPrice, qty, stop, tp);
                    Console.WriteLine($"[PAPER OPEN] {symbol} {decision} entry={entryPrice} qty={qty} sl={stop} tp={tp}");
                }

                // close check (simulate using last candle price)
                paper.CheckAndCloseByPrice(closes.Last());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{symbol} error: {ex.Message}");
            }
        }

        paper.PrintReport();
        mexc.Dispose();
    }
}
