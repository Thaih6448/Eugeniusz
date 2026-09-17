using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Eugeniusz
{
    public enum Kind { Choice = 0, Score = 1, Truth = 2 }

    [StructLayout(LayoutKind.Sequential)]
    public struct Result
    {
        public Kind Kind;
        public int Count, Choice;
        private int abstained;
        public double Value, Confidence, Certainty;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 26)] public double[] Probabilities;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 26)] public double[] Logits;
        public bool Abstained => abstained != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModelOptions
    {
        public uint ContextSize;
        public int Threads, GpuLayers;
        public static ModelOptions Default => Native.eg_llama_options_default();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeQuestion
    {
        public Kind Kind;
        public IntPtr Instructions, Criteria;
        public int Count;
        public double Temperature, MinConfidence;
    }

    internal sealed class EngineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal EngineHandle(IntPtr value) : base(true) { SetHandle(value); }
        protected override bool ReleaseHandle() { Native.eg_engine_destroy(handle); return true; }
    }

    internal static class Native
    {
        [DllImport("eugeniusz_llama", CallingConvention = CallingConvention.Cdecl)] internal static extern ModelOptions eg_llama_options_default();
        [DllImport("eugeniusz", CallingConvention = CallingConvention.Cdecl)] internal static extern uint eg_abi_version();
        [DllImport("eugeniusz", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr eg_last_error();
        [DllImport("eugeniusz", CallingConvention = CallingConvention.Cdecl)] internal static extern void eg_engine_destroy(IntPtr engine);
        [DllImport("eugeniusz", CallingConvention = CallingConvention.Cdecl)] internal static extern int eg_evaluate(EngineHandle engine, IntPtr state, ref NativeQuestion question, out Result result);
        [DllImport("eugeniusz", CallingConvention = CallingConvention.Cdecl)] internal static extern int eg_from_logits(Kind kind, double[] logits, int count, double temperature, double threshold, out Result result);
        [DllImport("eugeniusz_llama", CallingConvention = CallingConvention.Cdecl)] internal static extern int eg_llama_create(IntPtr path, ref ModelOptions options, out IntPtr engine, [Out] byte[] error, uint capacity);
        [DllImport("eugeniusz_llama", CallingConvention = CallingConvention.Cdecl)] internal static extern int eg_llama_create_with_system_prompt(IntPtr path, ref ModelOptions options, IntPtr systemPrompt, out IntPtr engine, [Out] byte[] error, uint capacity);
        [DllImport("eugeniusz_llama", CallingConvention = CallingConvention.Cdecl)] internal static extern int eg_llama_generate(EngineHandle engine, IntPtr systemPrompt, IntPtr prompt, uint maxTokens, [Out] byte[] output, uint outputCapacity, [Out] byte[] error, uint errorCapacity);
        internal static void Check(int status)
        {
            if (status != 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(eg_last_error()));
        }
    }

    internal sealed class Utf8 : IDisposable
    {
        public IntPtr Pointer { get; private set; }
        public Utf8(string value)
        {
            if (value == null || value.IndexOf('\0') >= 0) throw new ArgumentException("Text must be non-null and contain no embedded NUL");
            byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
            Pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, Pointer, bytes.Length);
        }
        public void Dispose() { if (Pointer != IntPtr.Zero) Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero; }
    }

    /// <summary>Owns one local model. Native inference calls are serialized. Dispose after use.</summary>
    public sealed class Engine : IDisposable
    {
        private readonly EngineHandle handle;
        private Engine(EngineHandle value) { handle = value; }
        public static Engine Load(string modelPath, ModelOptions? options = null)
            => Load(modelPath, options, null);

        public static Engine Load(string modelPath, ModelOptions? options, string? systemPrompt)
        {
            if (Native.eg_abi_version() != 1) throw new InvalidOperationException("Unsupported native ABI");
            using var path = new Utf8(modelPath);
            var config = options ?? ModelOptions.Default;
            var error = new byte[1024];
            IntPtr pointer;
            int status;
            if (systemPrompt == null)
                status = Native.eg_llama_create(path.Pointer, ref config, out pointer, error, (uint)error.Length);
            else
            {
                using var instructions = new Utf8(systemPrompt);
                status = Native.eg_llama_create_with_system_prompt(path.Pointer, ref config, instructions.Pointer, out pointer, error, (uint)error.Length);
            }
            if (status != 0) throw new InvalidOperationException(Encoding.UTF8.GetString(error).TrimEnd('\0'));
            return new Engine(new EngineHandle(pointer));
        }
        public static Result FromLogits(double[] logits, Kind kind = Kind.Choice, double temperature = 1, double threshold = 0)
        {
            if (logits == null) throw new ArgumentNullException(nameof(logits));
            Native.Check(Native.eg_from_logits(kind, logits, logits.Length, temperature, threshold, out var result));
            return result;
        }
        public Result Evaluate(string state, string instructions, string[] criteria, Kind kind = Kind.Choice, double temperature = 1, double threshold = 0)
        {
            if (criteria == null || criteria.Length > 26) throw new ArgumentException("Expected at most 26 criteria");
            using var stateText = new Utf8(state);
            using var questionText = new Utf8(instructions);
            var strings = new Utf8[criteria.Length];
            IntPtr pointers = IntPtr.Zero;
            try
            {
                if (criteria.Length > 0) pointers = Marshal.AllocHGlobal(IntPtr.Size * criteria.Length);
                for (int i = 0; i < criteria.Length; ++i)
                {
                    strings[i] = new Utf8(criteria[i]);
                    Marshal.WriteIntPtr(pointers, i * IntPtr.Size, strings[i].Pointer);
                }
                var question = new NativeQuestion { Kind = kind, Instructions = questionText.Pointer, Criteria = pointers,
                    Count = criteria.Length, Temperature = temperature, MinConfidence = threshold };
                Native.Check(Native.eg_evaluate(handle, stateText.Pointer, ref question, out var result));
                return result;
            }
            finally
            {
                foreach (var text in strings) text?.Dispose();
                if (pointers != IntPtr.Zero) Marshal.FreeHGlobal(pointers);
            }
        }
        public Result Choice(string state, string question, string[] criteria, double temperature = 1, double threshold = 0) => Evaluate(state, question, criteria, Kind.Choice, temperature, threshold);
        public Result Score(string state, string question, string[] levels, double temperature = 1, double threshold = 0) => Evaluate(state, question, levels, Kind.Score, temperature, threshold);
        public Result Truth(string state, string question, double temperature = 1, double threshold = 0) => Evaluate(state, question, Array.Empty<string>(), Kind.Truth, temperature, threshold);
        /// <summary>Bounded greedy text completion; separate from the typed decision API.</summary>
        public string Generate(string systemPrompt, string prompt, uint maxTokens = 1024)
        {
            if (maxTokens < 1 || maxTokens > 4096) throw new ArgumentOutOfRangeException(nameof(maxTokens));
            using var systemText = new Utf8(systemPrompt);
            using var input = new Utf8(prompt);
            var output = new byte[65536]; var error = new byte[1024];
            int status = Native.eg_llama_generate(handle, systemText.Pointer, input.Pointer, maxTokens, output, (uint)output.Length, error, (uint)error.Length);
            if (status != 0) throw new InvalidOperationException(Encoding.UTF8.GetString(error).TrimEnd('\0'));
            return Encoding.UTF8.GetString(output, 0, Array.IndexOf(output, (byte)0));
        }
        public void Dispose() => handle.Dispose();
    }
}
