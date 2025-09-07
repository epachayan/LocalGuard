using LocalGuard.DTOs;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LocalGuard.AI;

public sealed class RegistryOnnxScorer : IScorer<RegistryEventCtx>
{
    private readonly InferenceSession _session;
    private readonly RegistryFeatureExtractor _fx;
    private readonly string _in, _out;
    public double Threshold { get; }

    public RegistryOnnxScorer(string modelPath, RegistryFeatureExtractor fx, double threshold)
    {
        _session = new InferenceSession(modelPath);
        _fx = fx; Threshold = threshold;
        _in = _session.InputMetadata.Keys.First();
        _out = _session.OutputMetadata.Keys.First();
    }

    public (double score, string reason) Evaluate(RegistryEventCtx c)
    {
        var x = _fx.Extract(c.Hive, c.KeyPath, c.ValueName, c.ValueObj, c.Created, c.Modified, c.Deleted, c.Burst);
        var t = new DenseTensor<float>(x, new[] { 1, x.Length });
        var ins = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_in, t) };
        using var res = _session.Run(ins);
        var v = res.First(r => r.Name == _out).Value;

        float p;
        if (v is DenseTensor<float> dt && dt.Dimensions.SequenceEqual(new[] { 1, 2 })) p = dt[0, 1];
        else if (v is IEnumerable<float> en) p = en.First();
        else p = Convert.ToSingle(((IEnumerable<float>)v).First());

        var score = Math.Clamp(p * 100.0, 0.0, 100.0);
        return (score, $"p={p:0.00}");
    }
}
