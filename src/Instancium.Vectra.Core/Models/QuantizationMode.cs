namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Vector quantization mode of a collection. Fixed at collection creation and immutable afterwards.
/// Quantized collections must support two-pass search: approximate candidate selection
/// followed by exact recomputation over original float32 vectors.
/// </summary>
public enum QuantizationMode
{
    None = 0,
    Scalar8 = 1,
    Binary = 2,
}
