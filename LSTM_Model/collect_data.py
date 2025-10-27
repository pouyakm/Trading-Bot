import requests
import pandas as pd
import time
import sys
import os

def fetch_klines(symbol="BTCUSDT", interval="1m", limit=1000, endTime=None):
    url = "https://api.mexc.com/api/v3/klines"
    params = {"symbol": symbol, "interval": interval, "limit": limit}
    if endTime: params["endTime"] = endTime
    res = requests.get(url, params=params, timeout=10)
    res.raise_for_status()
    data = res.json()
    return data

def collect_data(symbol="BTCUSDT", interval="1m", max_candles=10000):
    all_data = []
    end_time = None
    while len(all_data) < max_candles:
        batch = fetch_klines(symbol, interval, 1000, endTime=end_time)
        if not batch: break
        df_batch = [{
            "open": float(x[1]),
            "high": float(x[2]),
            "low": float(x[3]),
            "close": float(x[4]),
            "volume": float(x[5])
        } for x in batch]
        all_data = df_batch + all_data
        end_time = int(batch[0][0]) - 1
        print(f"Collected {len(all_data)}")
        time.sleep(0.4)
    os.makedirs("data", exist_ok=True)
    df = pd.DataFrame(all_data)
    df.to_csv(f"data/{symbol}.csv", index=False)
    print(f"Saved data/{symbol}.csv ({len(df)} candles)")

if __name__ == "__main__":
    symbol = sys.argv[1] if len(sys.argv)>1 else "BTCUSDT"
    collect_data(symbol)
