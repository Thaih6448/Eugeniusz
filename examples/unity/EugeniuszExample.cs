using System;
using System.IO;
using System.Threading.Tasks;
using Eugeniusz;
using UnityEngine;

// Desktop Unity 6.6 example. Add Engine.cs or Eugeniusz.Managed.dll to the project.
public sealed class EugeniuszExample : MonoBehaviour
{
    public string ModelFile = "Qwen3-0.6B-Q4_0.gguf";
    public bool UseGpu = true;
    private bool stopped;

    private async void Start()
    {
        // Read Unity properties on the main thread before starting native work.
        string path = Path.Combine(Application.streamingAssetsPath, ModelFile);
        var options = ModelOptions.Default;
        options.ContextSize = 2048;
        options.GpuLayers = UseGpu ? 99 : 0;
        try
        {
            Result decision = await Task.Run(() =>
            {
                // A real service should keep the engine loaded across requests.
                using var engine = Engine.Load(path, options);
                return engine.Choice("The player asks where to buy a sword.",
                    "Which NPC should answer?",
                    new[] { "Blacksmith: weapons and armor", "Healer: health", "Innkeeper: rooms" },
                    threshold: 0.8);
            });
            // Unity's synchronization context resumes this continuation on the main thread.
            if (!stopped && this != null)
                Debug.Log($"NPC={decision.Choice}, confidence={decision.Confidence:F3}, review={decision.Abstained}");
        }
        catch (Exception error)
        {
            if (!stopped && this != null) Debug.LogException(error);
        }
    }

    private void OnDestroy() { stopped = true; }
}
