using System.Reflection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace CdsHelper.Support.Local.Helpers;

public sealed class OnnxDigitRecognizer : IDisposable
{
    private readonly InferenceSession _session;

    public OnnxDigitRecognizer()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("mnist_digit.onnx"));

        using var stream = asm.GetManifestResourceStream(resName)!;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        _session = new InferenceSession(bytes);
    }

    /// <summary>
    /// Recognize a single digit from a grayscale OpenCV Mat.
    /// Returns 0-9, or -1 if confidence is too low.
    /// </summary>
    public (int digit, float confidence) Recognize(Mat digitMat, float threshold = 0.5f)
    {
        // MNIST format: white digit on black background, 20x20 centered in 28x28
        using var binary = new Mat();
        if (digitMat.Type() != MatType.CV_8UC1)
            Cv2.CvtColor(digitMat, binary, ColorConversionCodes.BGR2GRAY);
        else
            digitMat.CopyTo(binary);

        // Ensure white-on-black (MNIST convention)
        // If the border is mostly white, invert
        double borderMean = 0;
        int count = 0;
        for (int x = 0; x < binary.Cols; x++)
        {
            borderMean += binary.At<byte>(0, x);
            borderMean += binary.At<byte>(binary.Rows - 1, x);
            count += 2;
        }
        for (int y = 1; y < binary.Rows - 1; y++)
        {
            borderMean += binary.At<byte>(y, 0);
            borderMean += binary.At<byte>(y, binary.Cols - 1);
            count += 2;
        }
        borderMean /= count;
        if (borderMean > 127)
            Cv2.BitwiseNot(binary, binary);

        // Resize digit to fit in 20x20, preserving aspect ratio
        int h = binary.Rows, w = binary.Cols;
        double scale = 20.0 / Math.Max(h, w);
        int newW = Math.Max(1, (int)(w * scale));
        int newH = Math.Max(1, (int)(h * scale));
        using var small = new Mat();
        Cv2.Resize(binary, small, new Size(newW, newH), interpolation: InterpolationFlags.Area);

        // Center in 28x28 black canvas
        var canvas = new Mat(28, 28, MatType.CV_8UC1, Scalar.Black);
        int offX = (28 - newW) / 2;
        int offY = (28 - newH) / 2;
        var roi = new Rect(offX, offY, newW, newH);
        small.CopyTo(new Mat(canvas, roi));

        // Build tensor, normalized to [-1, 1]
        var tensor = new DenseTensor<float>(new[] { 1, 1, 28, 28 });
        for (int y = 0; y < 28; y++)
        for (int x = 0; x < 28; x++)
            tensor[0, 0, y, x] = (canvas.At<byte>(y, x) / 255f - 0.5f) / 0.5f;

        canvas.Dispose();

        var inputs = new[] { NamedOnnxValue.CreateFromTensor("input", tensor) };
        using var results = _session.Run(inputs);
        var output = results.First().AsEnumerable<float>().ToArray();

        // Softmax
        float max = output.Max();
        var exp = output.Select(v => MathF.Exp(v - max)).ToArray();
        float sum = exp.Sum();
        var probs = exp.Select(v => v / sum).ToArray();

        int bestIdx = 0;
        for (int i = 1; i < probs.Length; i++)
            if (probs[i] > probs[bestIdx]) bestIdx = i;

        return probs[bestIdx] >= threshold ? (bestIdx, probs[bestIdx]) : (-1, probs[bestIdx]);
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
