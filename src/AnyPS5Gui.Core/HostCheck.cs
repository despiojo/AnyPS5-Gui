using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace AnyPS5Gui.Core;

public sealed record CpuFeature(string Name, bool Supported);

/// <summary>
/// Whether this PC can run converted games. They run natively, so the CPU needs the extensions of the
/// PS5's Zen 2 that game code uses without checking, and graphics need a Vulkan driver.
/// </summary>
public sealed record HostCheck(string CpuName, string CpuVendor, IReadOnlyList<CpuFeature> Features, bool HasVulkan)
{
    public bool IsIntel => CpuVendor == "GenuineIntel";
    public bool CpuSupported => Features.All(feature => feature.Supported);

    public static HostCheck Run()
    {
        if (!X86Base.IsSupported)
        {
            return new HostCheck(RuntimeInformation.ProcessArchitecture.ToString(), "", [new("x86-64", false)], HasVulkanLoader());
        }

        var (_, ebx, ecx, edx) = X86Base.CpuId(0, 0);
        var vendor = Ascii(ebx, edx, ecx);
        var leaf1Ecx = X86Base.CpuId(1, 0).Ecx;

        CpuFeature[] features =
        [
            new("AVX2", Avx2.IsSupported),
            new("FMA", Fma.IsSupported),
            new("F16C", (leaf1Ecx & (1 << 29)) != 0),
            new("BMI1", Bmi1.IsSupported),
            new("BMI2", Bmi2.IsSupported),
            new("LZCNT", Lzcnt.IsSupported),
            new("MOVBE", (leaf1Ecx & (1 << 22)) != 0),
        ];
        return new HostCheck(BrandString(), vendor, features, HasVulkanLoader());
    }

    private static string BrandString()
    {
        if ((uint)X86Base.CpuId(unchecked((int)0x80000000), 0).Eax < 0x80000004) return "";
        var name = new StringBuilder();
        for (var leaf = 0x80000002; leaf <= 0x80000004; leaf++)
        {
            var (eax, ebx, ecx, edx) = X86Base.CpuId(unchecked((int)leaf), 0);
            name.Append(Ascii(eax, ebx, ecx, edx));
        }
        return name.ToString().Trim('\0', ' ');
    }

    private static string Ascii(params int[] registers)
    {
        var bytes = new byte[registers.Length * 4];
        Buffer.BlockCopy(registers, 0, bytes, 0, bytes.Length);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static bool HasVulkanLoader() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "vulkan-1.dll"));
}
