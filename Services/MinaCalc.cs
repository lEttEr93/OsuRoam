using System.IO;
using System.Reflection;
using Wasmtime;

namespace OsuRoam.Services;

/// <summary>
/// Etterna MinaCalc 的 WASM 封装。输出 Overall + 七个 skillset
/// （Stream/Jumpstream/Handstream/Stamina/JackSpeed/Chordjack/Technical）。
/// wasm 与调用约定取自 osumania_map_analyser（MIT）。
///
/// 两套文件：
///   4K     → Resources/minacalc.wasm（0.72.0，导出名被压缩：m/o/q/r）
///   非 4K  → Resources/minacalc-75.wasm（0.75.0，导出名完整，导入走 WASI）
///
/// 每个 wasm 实例都有内部全局状态，重复调用会互相污染，所以每次计算都新建实例。
/// </summary>
public static class MinaCalc
{
    private const int OutputCount = 8;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, (Engine Engine, Wasmtime.Module Module)?> Modules = [];

    public static bool IsAvailable => load(ResourceForKeys(4)) is not null;

    public static string? FailureReason { get; private set; }

    private static string ResourceForKeys(int keys) =>
        keys == 4 ? "OsuRoam.Resources.minacalc.wasm" : "OsuRoam.Resources.minacalc-75.wasm";

    public static double[]? Compute(int keys, uint[] rowMasks, float[] rowTimes, float musicRate = 1.0f, float scoreGoal = 0.93f)
    {
        if (rowMasks.Length == 0 || rowMasks.Length != rowTimes.Length)
            return null;

        (Engine Engine, Wasmtime.Module Module)? loaded = load(ResourceForKeys(keys));
        if (loaded is null)
            return null;

        lock (Sync)
        {
            try
            {
                Linker linker = new(loaded.Value.Engine);
                registerImports(linker);
                Store store = new(loaded.Value.Engine);
                Instance instance = linker.Instantiate(store, loaded.Value.Module);

                // WASI 构建需要先跑静态初始化，否则 malloc 尚未初始化。
                instance.GetFunction("__wasm_call_ctors")?.Invoke();

                Memory? memory = instance.GetMemory("memory") ?? instance.GetMemory("m");
                Function? malloc = instance.GetFunction("malloc") ?? instance.GetFunction("_malloc") ?? instance.GetFunction("q");
                Function? free = instance.GetFunction("free") ?? instance.GetFunction("_free") ?? instance.GetFunction("r");
                Function? compute = instance.GetFunction("minacalc_compute") ?? instance.GetFunction("_minacalc_compute") ?? instance.GetFunction("o");
                if (memory is null || malloc is null || compute is null)
                {
                    FailureReason = "缺少必要导出（memory/malloc/minacalc_compute）";
                    AppLog.Error($"MinaCalc 不可用：{FailureReason}");
                    return null;
                }

                int ptrMasks = malloc.Invoke(rowMasks.Length * sizeof(uint)) is int p1 ? p1 : 0;
                int ptrTimes = malloc.Invoke(rowTimes.Length * sizeof(float)) is int p2 ? p2 : 0;
                int ptrOut = malloc.Invoke(OutputCount * sizeof(float)) is int p3 ? p3 : 0;
                if (ptrMasks <= 0 || ptrTimes <= 0 || ptrOut <= 0)
                    return null;

                try
                {
                    for (int index = 0; index < rowMasks.Length; index++)
                    {
                        memory.Write((uint)ptrMasks + (uint)(index * sizeof(uint)), rowMasks[index]);
                        memory.Write((uint)ptrTimes + (uint)(index * sizeof(float)), rowTimes[index]);
                    }

                    int ok = compute.Invoke(keys, musicRate, scoreGoal, ptrMasks, ptrTimes, rowMasks.Length, ptrOut) is int value ? value : 0;
                    if (ok == 0)
                        return null;

                    double[] result = new double[OutputCount];
                    for (int index = 0; index < OutputCount; index++)
                        result[index] = memory.Read<float>((uint)ptrOut + (uint)(index * sizeof(float)));
                    return result;
                }
                finally
                {
                    free?.Invoke(ptrMasks);
                    free?.Invoke(ptrTimes);
                    free?.Invoke(ptrOut);
                }
            }
            catch (Exception exception)
            {
                FailureReason = exception.Message;
                AppLog.Error("MinaCalc 计算失败", exception);
                return null;
            }
        }
    }

    private static (Engine Engine, Wasmtime.Module Module)? load(string resource)
    {
        lock (Sync)
        {
            if (Modules.TryGetValue(resource, out (Engine Engine, Wasmtime.Module Module)? cached))
                return cached;

            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
                if (stream is null)
                {
                    Modules[resource] = null;
                    FailureReason = $"未找到内嵌资源 {resource}";
                    return null;
                }

                Engine engine = new();
                Wasmtime.Module module = Wasmtime.Module.FromStream(engine, resource, stream);
                Modules[resource] = (engine, module);
                return (engine, module);
            }
            catch (Exception exception)
            {
                Modules[resource] = null;
                FailureReason = exception.Message;
                AppLog.Error($"MinaCalc 加载失败：{resource}", exception);
                return null;
            }
        }
    }

    /// <summary>
    /// 两套 wasm 的导入都注册一遍：0.72 用压缩模块 a，0.75 用 env / wasi_snapshot_preview1。
    /// 用不到的定义会被忽略。
    /// </summary>
    private static void registerImports(Linker linker)
    {
        linker.DefineFunction("a", "a", (int a1, int a2, int a3, int a4) => 0);
        linker.DefineFunction("a", "b", () => { });
        linker.DefineFunction("a", "c", (int a1, int a2, int a3) => { });
        linker.DefineFunction("a", "d", (int a1) => 16);
        linker.DefineFunction("a", "e", (int a1, int a2) => 0);
        linker.DefineFunction("a", "f", (int a1, int a2) => 0);
        linker.DefineFunction("a", "g", (int a1) => 0);
        linker.DefineFunction("a", "h", (int a1, int a2, int a3, int a4) => 0);
        linker.DefineFunction("a", "i", (int a1) => 1);
        linker.DefineFunction("a", "j", (int a1, int a2, int a3) => a1);
        linker.DefineFunction("a", "k", (int a1, int a2, int a3, int a4, int a5) => 0);
        linker.DefineFunction("a", "l", (int a1, int a2, int a3, int a4, int a5) => 0);

        linker.DefineFunction("env", "_abort_js", () => { });
        linker.DefineFunction("env", "_tzset_js", (int a1, int a2, int a3, int a4) => { });
        linker.DefineFunction("env", "emscripten_resize_heap", (int size) => 1);
        linker.DefineFunction("wasi_snapshot_preview1", "fd_write", (int a1, int a2, int a3, int a4) => 0);
        linker.DefineFunction("wasi_snapshot_preview1", "fd_read", (int a1, int a2, int a3, int a4) => 0);
        linker.DefineFunction("wasi_snapshot_preview1", "fd_close", (int a1) => 0);
        linker.DefineFunction("wasi_snapshot_preview1", "fd_seek", (int a1, long a2, int a3, int a4) => 0);
        linker.DefineFunction("wasi_snapshot_preview1", "environ_sizes_get", (int a1, int a2) => 0);
        linker.DefineFunction("wasi_snapshot_preview1", "environ_get", (int a1, int a2) => 0);
    }
}
