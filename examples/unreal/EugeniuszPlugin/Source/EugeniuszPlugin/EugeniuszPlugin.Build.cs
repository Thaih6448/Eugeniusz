using System.IO;
using UnrealBuildTool;

public class EugeniuszPlugin : ModuleRules
{
    public EugeniuszPlugin(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine" });
        string platform = Target.Platform.ToString();
        string sdk = Path.Combine(PluginDirectory, "ThirdParty", "Eugeniusz", platform);
        PublicSystemIncludePaths.Add(Path.Combine(sdk, "include"));
        if (Target.Platform == UnrealTargetPlatform.Win64)
        {
            foreach (string name in new[] { "eugeniusz", "eugeniusz_llama" })
            {
                PublicAdditionalLibraries.Add(Path.Combine(sdk, "lib", name + ".lib"));
                PublicDelayLoadDLLs.Add(name + ".dll");
            }
            foreach (string dll in Directory.GetFiles(Path.Combine(sdk, "bin"), "*.dll"))
                RuntimeDependencies.Add("$(TargetOutputDir)/" + Path.GetFileName(dll), dll);
        }
        else if (Target.Platform == UnrealTargetPlatform.Linux || Target.Platform == UnrealTargetPlatform.Mac)
        {
            string extension = Target.Platform == UnrealTargetPlatform.Mac ? ".dylib" : ".so";
            foreach (string name in new[] { "eugeniusz", "eugeniusz_llama" })
                PublicAdditionalLibraries.Add(Path.Combine(sdk, "lib", "lib" + name + extension));
            foreach (string library in Directory.GetFiles(Path.Combine(sdk, "lib"), "*" + extension + "*"))
                RuntimeDependencies.Add("$(TargetOutputDir)/" + Path.GetFileName(library), library);
        }
        else throw new BuildException("Eugeniusz example supports desktop Win64, Linux and Mac only.");
    }
}
