import sys
import pandas as pd
import numpy as np
import json
import os
from sklearn.preprocessing import MinMaxScaler
from tensorflow.keras.models import Sequential, load_model
from tensorflow.keras.layers import LSTM, Dense, Dropout

LOOKBACK = 60

def build_model(input_shape):
    model = Sequential([
        LSTM(64, return_sequences=True, input_shape=input_shape),
        Dropout(0.2),
        LSTM(32),
        Dropout(0.2),
        Dense(16, activation='relu'),
        Dense(1, activation='sigmoid')
    ])
    model.compile(optimizer='adam', loss='binary_crossentropy', metrics=['accuracy'])
    return model

def train_model(csv_path, model_path):
    df = pd.read_csv(csv_path)
    if len(df) < LOOKBACK + 2:
        print(json.dumps({"error":"not enough data"}))
        return
    data = df[['open','high','low','close','volume']].values
    scaler = MinMaxScaler()
    scaled = scaler.fit_transform(data)
    X, y = [], []
    for i in range(LOOKBACK, len(scaled)-1):
        X.append(scaled[i-LOOKBACK:i])
        y.append(1 if data[i+1,3] > data[i,3] else 0)
    X, y = np.array(X), np.array(y)
    model = build_model((X.shape[1], X.shape[2]))
    model.fit(X, y, epochs=8, batch_size=32, validation_split=0.1, verbose=0)
    model.save(model_path)
    # save scaler params for production if needed (not used here)
    print(json.dumps({"trained": True}))

def predict(csv_path, model_path):
    df = pd.read_csv(csv_path)
    if len(df) < LOOKBACK:
        print(json.dumps({"error":"not enough rows for predict"}))
        return
    data = df[['open','high','low','close','volume']].values
    from sklearn.preprocessing import MinMaxScaler
    scaler = MinMaxScaler()
    scaled = scaler.fit_transform(data)
    X = np.array([scaled[-LOOKBACK:]])
    model = load_model(model_path)
    prob = float(model.predict(X, verbose=0)[0][0])
    print(json.dumps({"prob_up": round(prob,4)}))

if __name__ == "__main__":
    if len(sys.argv) < 4:
        print("Usage: train_and_predict.py [train|predict] data.csv model.h5")
        sys.exit(1)
    action = sys.argv[1]
    csv_path = sys.argv[2]
    model_path = sys.argv[3]
    if action == "train":
        train_model(csv_path, model_path)
    elif action == "predict":
        predict(csv_path, model_path)
