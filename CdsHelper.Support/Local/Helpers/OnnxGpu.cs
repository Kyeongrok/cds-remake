using Microsoft.ML.OnnxRuntime;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// ONNX 모델을 GPU(DirectML — DirectX 12 가 되는 NVIDIA · AMD · 인텔 모두)로 먼저 열고, 안 되면 CPU 로 연다.
/// 도시 그림 · 선박 업스케일링이 함께 쓴다.
/// </summary>
/// <remarks>
/// DirectML 은 메모리 패턴 · 병렬 실행을 못 쓴다(ONNX Runtime 문서). DirectML.dll 은 실행 프로젝트(CostaDelSol.Play)가
/// Microsoft.AI.DirectML 을 직접 걸어야 실행 폴더로 복사된다 — 없으면 GPU 열기가 실패해 CPU 로 물러선다.
/// </remarks>
public static class OnnxGpu
{
    /// <summary>모델을 연다. <paramref name="gpu"/> 는 GPU 로 열었는지.</summary>
    /// <param name="cpuThreads">CPU 로 열 때 쓸 실 수. 0 이면 ONNX Runtime 기본.</param>
    /// <param name="allowGpu">false 면 GPU 를 건너뛴다(GPU 로 돌리다 실패해 다시 열 때).</param>
    public static InferenceSession Open(string modelPath, out bool gpu, int cpuThreads = 0, bool allowGpu = true)
    {
        if (allowGpu)
        {
            try
            {
                var options = new SessionOptions
                {
                    EnableMemoryPattern = false,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                };
                options.AppendExecutionProvider_DML(0);
                var session = new InferenceSession(modelPath, options);
                gpu = true;
                return session;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnnxGpu] GPU 를 못 연다 — CPU 로: {ex.Message}");
            }
        }

        gpu = false;
        if (cpuThreads <= 0) return new InferenceSession(modelPath);
        return new InferenceSession(modelPath, new SessionOptions
        {
            IntraOpNumThreads = cpuThreads,
            InterOpNumThreads = 1,
        });
    }
}
