# make_model.py -- creates models/ids.onnx for LocalGuard (14-dim input)
# offline: uses synthetic data if you don't have logs yet
import os, numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import train_test_split

# 1) synth data: 14-dim features, label ~ "weirdness"
rng = np.random.default_rng(42)
N = 4000
X = rng.normal(0, 1, size=(N, 14)).astype(np.float32)
# make some features "suspicious": ext buckets (0..7), flags near the end
ext_bucket = rng.integers(0, 8, size=(N,))
for i in range(N):
    X[i, ext_bucket[i]] = 1.0  # one-hot ext bucket
# flags-ish: exec/startup/burst at the end (columns 11,12,13)
X[:, 11] = (rng.random(N) < 0.1).astype(np.float32)   # isExec
X[:, 12] = (rng.random(N) < 0.05).astype(np.float32)  # inStartup
X[:, 13] = (rng.random(N) < 0.15).astype(np.float32)  # burst
# label: mix of linear + noise
w = np.array([0.6,0.5,0.5,0.4,0.1,0.1,0.1,0.1,  # buckets
              0.8,0.7,0.3,  # nameEntropy, contentEntropy, sizeLog
              1.5, 2.0, 1.0], dtype=np.float32)  # exec/startup/burst
y_prob = 1/(1+np.exp(-(X @ w + rng.normal(0, 0.5, N))))
y = (y_prob > 0.5).astype(np.int32)

Xtr, Xte, ytr, yte = train_test_split(X, y, test_size=0.2, random_state=7)
clf = LogisticRegression(max_iter=200).fit(Xtr, ytr)

# 2) export to ONNX
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType
onnx_model = convert_sklearn(
    clf,
    initial_types=[('input', FloatTensorType([None, 14]))],
    target_opset=13
)
os.makedirs("models", exist_ok=True)
with open("models/ids.onnx", "wb") as f:
    f.write(onnx_model.SerializeToString())
print("Wrote models/ids.onnx")
