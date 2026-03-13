import pandas as pd
import numpy as np
import torch
import torch.nn as nn
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import roc_auc_score
from torch.utils.data import Dataset, DataLoader

CSV_PATH = "features.csv"

SEQ_LEN = 100
FEATURES = 13
BATCH_SIZE = 64
EPOCHS = 20
LR = 0.001

TRAIN_SPLIT = 0.7
VAL_SPLIT = 0.15

# =========================
# Load Data
# =========================

df = pd.read_csv(CSV_PATH)

X = df.iloc[:, :-1].values
y = df.iloc[:, -1].values

samples = X.shape[0]

# reshape to sequences
X = X.reshape(samples, SEQ_LEN, FEATURES)

# =========================
# Chronological split
# =========================

train_end = int(samples * TRAIN_SPLIT)
val_end = int(samples * (TRAIN_SPLIT + VAL_SPLIT))

X_train = X[:train_end]
y_train = y[:train_end]

X_val = X[train_end:val_end]
y_val = y[train_end:val_end]

X_test = X[val_end:]
y_test = y[val_end:]

# =========================
# Normalize features
# =========================

scaler = StandardScaler()

X_train_flat = X_train.reshape(-1, FEATURES)
scaler.fit(X_train_flat)

def scale_data(X):
    shape = X.shape
    X = X.reshape(-1, FEATURES)
    X = scaler.transform(X)
    return X.reshape(shape)

X_train = scale_data(X_train)
X_val = scale_data(X_val)
X_test = scale_data(X_test)

# =========================
# Dataset
# =========================

class MarketDataset(Dataset):

    def __init__(self, X, y):
        self.X = torch.tensor(X, dtype=torch.float32)
        self.y = torch.tensor(y, dtype=torch.float32)

    def __len__(self):
        return len(self.X)

    def __getitem__(self, idx):
        return self.X[idx], self.y[idx]

train_loader = DataLoader(MarketDataset(X_train, y_train), batch_size=BATCH_SIZE, shuffle=True)
val_loader = DataLoader(MarketDataset(X_val, y_val), batch_size=BATCH_SIZE)
test_loader = DataLoader(MarketDataset(X_test, y_test), batch_size=BATCH_SIZE)

# =========================
# Model
# =========================

class CNNLSTM(nn.Module):

    def __init__(self):
        super().__init__()

        self.conv = nn.Conv1d(
            in_channels=FEATURES,
            out_channels=32,
            kernel_size=3
        )

        self.lstm = nn.LSTM(
            input_size=32,
            hidden_size=64,
            batch_first=True
        )

        self.fc = nn.Linear(64, 1)

    def forward(self, x):

        # x shape: (batch, seq, features)

        x = x.transpose(1, 2)      # (batch, features, seq)

        x = torch.relu(self.conv(x))

        x = x.transpose(1, 2)      # (batch, seq, channels)

        out, _ = self.lstm(x)

        x = out[:, -1, :]          # last timestep

        x = self.fc(x)

        return torch.sigmoid(x).squeeze()

model = CNNLSTM()

criterion = nn.BCELoss()
optimizer = torch.optim.Adam(model.parameters(), lr=LR)

# =========================
# Training
# =========================

for epoch in range(EPOCHS):

    model.train()
    total_loss = 0

    for X_batch, y_batch in train_loader:

        preds = model(X_batch)

        loss = criterion(preds, y_batch)

        optimizer.zero_grad()
        loss.backward()
        optimizer.step()

        total_loss += loss.item()

    print(f"Epoch {epoch+1} Loss: {total_loss:.4f}")

    # validation AUC
    model.eval()

    preds = []
    targets = []

    with torch.no_grad():
        for X_batch, y_batch in val_loader:

            p = model(X_batch)

            preds.extend(p.numpy())
            targets.extend(y_batch.numpy())

    auc = roc_auc_score(targets, preds)

    print(f"Validation AUC: {auc:.4f}")

# =========================
# Test Evaluation
# =========================

model.eval()

preds = []
targets = []

with torch.no_grad():
    for X_batch, y_batch in test_loader:

        p = model(X_batch)

        preds.extend(p.numpy())
        targets.extend(y_batch.numpy())

auc = roc_auc_score(targets, preds)

print("\nTest AUC:", auc)

# =========================
# Save Model
# =========================

torch.save(model.state_dict(), "cnn_lstm_model.pt")