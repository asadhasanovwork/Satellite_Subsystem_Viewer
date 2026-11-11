using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;

public static class NativePhysics
{
    /// <summary>Shared live/preview finite-burn integration. Previews supply copied fuel buffers.
    /// Only finite entries consume fuel; returned delta-v is in world units/s.</summary>
    [DllImport("PhysicsPlugin", EntryPoint = "BatchTwoBodyIntegrateMuExWithFuel", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BatchTwoBodyIntegrateMuExWithFuel(
        [In, Out] double3[] positions, [In, Out] double3[] velocities,
        [In] double[] masses, [In] Vector3[] thrusts,
        [In] float[] dragCoeffs, [In] float[] areasUU,
        [In] sbyte[] normalSign, [In] byte[] isThrusting, [In, Out] sbyte[] latchedParityIO,
        int count, double muUnity, float totalDt, int substeps, [Out] double[] deltaVOut,
        [In] double[] dryMasses, [In, Out] double[] fuelMasses,
        [In] double[] ispSeconds, [In] byte[] finiteFuel);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string path);

    /// <summary>Folder that holds PhysicsPlugin.dll and its MinGW runtime DLLs.</summary>
    static string PluginDir()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "ThirdParty/OrbitalControl/Plugins/x86_64"));
#else
        string a = Path.Combine(Application.dataPath, "Plugins/x86_64");
        return Directory.Exists(a) ? a : Path.Combine(Application.dataPath, "Plugins");
#endif
    }

    static NativePhysics()
    {
        string dir = PluginDir();
        // Preload the MinGW runtime next to the plugin so Windows can resolve it.
        SetDllDirectory(dir);
        foreach (var dep in new[] { "libwinpthread-1.dll", "libgcc_s_seh-1.dll", "libstdc++-6.dll", "PhysicsPlugin.dll" })
        {
            string full = Path.Combine(dir, dep);
            if (!File.Exists(full)) continue;
            if (LoadLibrary(full) == IntPtr.Zero)
                Debug.LogError($"[NATIVE PHYSICS] Could not load {dep} (Win32 error {Marshal.GetLastWin32Error()})");
        }
    }

    // NativePhysics.cs
    [DllImport("PhysicsPlugin", EntryPoint = "BatchTwoBodyIntegrateMuEx", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BatchTwoBodyIntegrateMuEx(
    [In, Out] Unity.Mathematics.double3[] positions,
    [In, Out] Unity.Mathematics.double3[] velocities,
    [In] double[] masses,
    [In] UnityEngine.Vector3[] thrusts,
    [In] float[] dragCoeffs,
    [In] float[] areasUU,
    [In] sbyte[] normalSign,      // 0 free, +1 Normal, -1 AntiNormal
    [In] byte[] isThrusting,
    [In, Out] sbyte[] latchedParityIO,
    int count,
    double muUnity,
    float totalDt,
    int substeps
);

    /// <summary>Returns applied thrust delta-v in world units/s, excluding gravity and drag.</summary>
    [DllImport("PhysicsPlugin", EntryPoint = "BatchTwoBodyIntegrateMuExWithDeltaV", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BatchTwoBodyIntegrateMuExWithDeltaV(
        [In, Out] double3[] positions,
        [In, Out] double3[] velocities,
        [In] double[] masses,
        [In] Vector3[] thrusts,
        [In] float[] dragCoeffs,
        [In] float[] areasUU,
        [In] sbyte[] normalSign,
        [In] byte[] isThrusting,
        [In, Out] sbyte[] latchedParityIO,
        int count,
        double muUnity,
        float totalDt,
        int substeps,
        [Out] double[] deltaVOut);

}

