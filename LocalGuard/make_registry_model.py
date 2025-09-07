# make_registry_model.py
# Builds models/ids_registry.onnx for LocalGuard registry events (15 features)
import os
import json
import math
import numpy as np

from sklearn.model_selection import train_test_split
from sklearn.neural_network import MLPClassifier
from sklearn.metrics import roc_auc_score, classification_report
from sklearn.preprocessing import StandardScaler
from sklearn.pipeline import Pipeline

from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType

np.random.seed(42)

# -----------------------------
# Feature layout (must match C#):
# 0..5  : one-hot bucket [HKCU/Run, HKLM/Run, HKCU/RunOnce, HKLM/RunOnce, HKCR/*/open/command, Other]
# 6     : isString
# 7     : isBinary
# 8     : isNumber
# 9     : entropy (0..8)
# 10    : lenLog
# 11    : created
# 12    : modified
# 13    : deleted
# 14    : burst
# -----------------------------

N_FEATS = 15

def synth_dataset(n_pos=1500, n_neg=3000):
    """Make a reasonable synthetic dataset for first run.
       Positive examples look like likely persistence/hijack with create/modify."""
    X_pos = []
    # Positives: autostart buckets, string values (paths), created/modified, higher len & some entropy
    for _ in range(n_pos):
        x = np.zeros(N_FEATS, dtype=np.float32)
        # bucket: bias towards persistence buckets 0..4
        b = np.random.choice([0,1,2,3,4], p=[0.25,0.25,0.2,0.2,0.1])
        x[b] = 1.0
        # values typical of command lines / paths
        x[6] = 1.0  # isString
        x[7] = 0.0  # isBinary
        x[8] = 0.0  # isNumber
        # entropy: command lines have modest entropy (1.5..4.0)
        x[9]  = np.random.uniform(1.5, 4.0)
        # lenLog: path-ish 40..200 bytes
        length = np.random.randint(40, 200)
        x[10] = math.log10(length + 1)
        # change flags: mostly created or modified
        flags = np.random.choice(["c","m","cm"], p=[0.45,0.45,0.10])
        x[11] = 1.0 if "c" in flags else 0.0
        x[12] = 1.0 if "m" in flags else 0.0
        x[13] = 0.0  # not deletions usually
        # bursts sometimes true
        x[14] = np.random.choice([0.0,1.0], p=[0.7,0.3])

        X_pos.append(x)

    X_neg = []
    # Negatives: "Other" bucket, numbers/binary, deletes, low entropy, tiny or very big length
    for _ in range(n_neg):
        x = np.zeros(N_FEATS, dtype=np.float32)
        # bucket: Other
        x[5] = 1.0
        # random value types skewed away from string
        typ = np.random.choice(["bin","num","str"], p=[0.5,0.3,0.2])
        x[6] = 1.0 if typ == "str" else 0.0
        x[7] = 1.0 if typ == "bin" else 0.0
        x[8] = 1.0 if typ == "num" else 0.0
        # entropy: many configs low (0..1.5)
        x[9]  = np.random.uniform(0.0, 1.5)
        # lenLog: tiny (0..20 bytes) or random
        length = np.random.choice([np.random.randint(0,20), np.random.randint(1,80)])
        x[10] = math.log10(length + 1)
        # change flags: more deletes or single modified, fewer created
        choice = np.random.choice(["none","m","d"], p=[0.5,0.3,0.2])
        x[11] = 1.0 if choice == "c" else 0.0
        x[12] = 1.0 if choice == "m" else 0.0
        x[13] = 1.0 if choice == "d" else 0.0
        # rarely burst
        x[14] = np.random.choice([0.0,1.0], p=[0.9,0.1])

        X_neg.append(x)

    X = np.vstack([X_pos, X_neg]).astype(np.float32)
    y = np.hstack([np.ones(len(X_pos)), np.zeros(len(X_neg))]).astype(np.int64)
    return X, y

def load_or_synth():
    """
    Optional: if you later log real registry feature vectors into a CSV (15 cols + label),
    load them here. For now we synthesize.
    """
    return synth_dataset()

def train_and_export(X, y, onnx_path):
    X_tr, X_te, y_tr, y_te = train_test_split(X, y, test_size=0.25, stratify=y, random_state=42)

    # Scaler + small MLP for non-linear interactions; outputs probabilities
    clf = Pipeline(steps=[
        ("scaler", StandardScaler(with_mean=True, with_std=True)),
        ("mlp", MLPClassifier(hidden_layer_sizes=(16,), activation="relu",
                              solver="adam", alpha=1e-4, max_iter=300,
                              random_state=42))
    ])
    clf.fit(X_tr, y_tr)

    # Quick metrics
    prob = clf.predict_proba(X_te)[:,1]
    auc  = roc_auc_score(y_te, prob)
    print(f"AUC={auc:.3f}")
    print(classification_report(y_te, (prob>=0.5).astype(int), digits=3))

    # Convert to ONNX
    os.makedirs(os.path.dirname(onnx_path), exist_ok=True)
    onnx_model = convert_sklearn(
        clf,
        initial_types=[("input", FloatTensorType([None, X.shape[1]]))],
        target_opset=13
    )
    # Rename output to "prob" for clarity (optional)
    # skl2onnx names vary; but C# takes first output. We'll keep default.

    with open(onnx_path, "wb") as f:
        f.write(onnx_model.SerializeToString())

    # Save a small sidecar with feature order (useful for audits)
    meta = {
        "features": [
            "bucket_HKCU_Run","bucket_HKLM_Run","bucket_HKCU_RunOnce","bucket_HKLM_RunOnce","bucket_HKCR_open_cmd","bucket_Other",
            "isString","isBinary","isNumber","entropy","lenLog","created","modified","deleted","burst"
        ],
        "input_name": "input",
        "output_note": "classifier probability (class=1 suspicious)"
    }
    with open(os.path.join(os.path.dirname(onnx_path), "ids_registry.meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)

    print(f"ONNX saved -> {onnx_path}")

def main():
    onnx_path = os.path.join("models", "ids_registry.onnx")
    X, y = load_or_synth()
    train_and_export(X, y, onnx_path)

if __name__ == "__main__":
    main()
