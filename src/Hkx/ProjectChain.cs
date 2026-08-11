using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;




public sealed class ProjectChain
{
    public sealed class Link
    {
        public string Role = "";
        public string Declared = "";
        public string Resolved = "";
        public bool Exists;
        public string Note = "";
    }

    public string Root = "";
    public readonly List<Link> Links = new();
    public readonly List<string> Animations = new();
    public readonly List<string> Bones = new();
    public readonly List<string> Problems = new();




    public HkxSkeleton? Skeleton;
    public string SkeletonPath = "";

    public static ProjectChain Resolve(
        string anyHkxPath, Func<string, BehaviourGraphModel?>? modelReader = null)
    {
        var chain = new ProjectChain();
        BehaviourGraphModel? ReadModel(string path) =>
            modelReader == null ? Read(path, chain) : Read(path, chain, modelReader);
        string dir = Path.GetDirectoryName(Path.GetFullPath(anyHkxPath)) ?? "";



        string leaf = Path.GetFileName(dir);
        chain.Root = leaf.Equals("Behaviors", StringComparison.OrdinalIgnoreCase)
                  || leaf.Equals("Characters", StringComparison.OrdinalIgnoreCase)
                  || leaf.Equals("CharacterAssets", StringComparison.OrdinalIgnoreCase)
                  || leaf.Equals("Animations", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(dir) ?? dir
            : dir;

        var projectCandidates = Directory
            .EnumerateFiles(chain.Root, "*.hkx", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => (Path: path, Model: ReadModel(path)))
            .Where(candidate => candidate.Model?.Objects.Any(
                o => o.Class == "hkbProjectStringData") == true)
            .ToList();
        if (projectCandidates.Count == 0)
        {
            chain.Problems.Add(
                $"no project .hkx containing hkbProjectStringData directly under {chain.Root}");
            return chain;
        }
        if (projectCandidates.Count > 1)
        {
            chain.Problems.Add(
                "ambiguous project files containing hkbProjectStringData: " +
                string.Join(", ", projectCandidates.Select(c => Path.GetFileName(c.Path))));
            return chain;
        }

        string projectFile = projectCandidates[0].Path;
        var project = projectCandidates[0].Model;
        chain.Add("project", Path.GetFileName(projectFile), projectFile);
        string characterRel = project?.Objects
            .FirstOrDefault(o => o.Class == "hkbProjectStringData")?.Strings("characterFilenames")
            .FirstOrDefault() ?? "";

        if (characterRel.Length == 0)
        {
            chain.Problems.Add("the project names no character file");
            return chain;
        }

        string characterPath = ResolvePath(chain.Root, characterRel);
        chain.Add("character", characterRel, characterPath);
        if (!File.Exists(characterPath)) return chain;

        var character = ReadModel(characterPath);
        var strings = character?.Objects.FirstOrDefault(o => o.Class == "hkbCharacterStringData");
        if (strings == null)
        {
            chain.Problems.Add("the character file has no hkbCharacterStringData");
            return chain;
        }



        string behaviourRel = strings.Str("behaviorFilename");
        if (behaviourRel.Length > 0)
            chain.Add("behaviour", behaviourRel, ResolvePath(chain.Root, behaviourRel));
        else
            chain.Problems.Add("the character names no behaviour file");

        string rigRel = strings.Str("rigName");
        if (rigRel.Length > 0)
        {
            string rigPath = ResolvePath(chain.Root, rigRel);
            var link = chain.Add("skeleton", rigRel, rigPath);
            if (link.Exists)
            {
                try
                {
                    var skeleton = new HkxBinaryReader().ReadSkeleton(rigPath);
                    chain.Bones.AddRange(skeleton.BoneNames);
                    chain.Skeleton = skeleton;
                    chain.SkeletonPath = rigPath;
                    link.Note = $"{skeleton.BoneNames.Count} bones";
                }
                catch (Exception ex)
                {
                    link.Note = "could not read: " + ex.Message.Split('\n')[0];
                }
            }
        }
        else
        {
            chain.Problems.Add("the character names no skeleton");
        }

        foreach (string anim in DeclaredAnimations(strings))
        {
            chain.Animations.Add(anim);
            if (File.Exists(ResolvePath(chain.Root, anim))) continue;





            string? lender = BorrowedFrom(anim);
            chain.Problems.Add(lender != null
                ? $"missing animation, borrowed from {lender}: {anim}. Extract {lender} alongside " +
                  "this character and it resolves."
                : "missing animation: " + anim);
        }

        return chain;
    }




    public static string? BorrowedFrom(string animation)
    {
        var parts = animation.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        int lastUp = Array.LastIndexOf(parts, "..");
        return lastUp >= 0 && lastUp + 1 < parts.Length ? parts[lastUp + 1] : null;
    }




    public static List<string> DeclaredAnimations(HkObject characterStringData)
    {
        var all = new List<string>(characterStringData.Strings("animationNames"));
        foreach (string name in characterStringData.Strings("animationBundleNameData"))
            if (!all.Contains(name)) all.Add(name);
        return all;
    }



    public static string AnimationKey(string declared)
        => Path.ChangeExtension(declared.Replace('/', '\\'), null).ToLowerInvariant();


    public static string ResolvePath(string baseDir, string relative)
    {
        string cleaned = relative.Replace('\\', Path.DirectorySeparatorChar)
                                 .Replace('/', Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(baseDir, cleaned));
        if (File.Exists(full)) return full;

        string swapped = Path.ChangeExtension(full, ".hkx");
        return File.Exists(swapped) ? swapped : full;
    }

    private Link Add(string role, string declared, string resolved)
    {
        var link = new Link
        {
            Role = role,
            Declared = declared,
            Resolved = resolved,
            Exists = File.Exists(resolved),
        };
        if (!link.Exists) Problems.Add($"{role} file is missing: {declared}");
        Links.Add(link);
        return link;
    }

    private static BehaviourGraphModel? Read(string hkxPath, ProjectChain chain) =>
        Read(hkxPath, chain, path =>
        {
            string xml = HkxTextEdit.TextOf(path);
            if (xml.Length == 0)
            {
                chain.Problems.Add($"could not read {Path.GetFileName(hkxPath)}: it holds a class this " +
                                   "build cannot describe");
                return null;
            }

            return BehaviourGraphModel.Parse(xml);
        });

    private static BehaviourGraphModel? Read(
        string hkxPath, ProjectChain chain, Func<string, BehaviourGraphModel?> reader)
    {
        try
        {
            return reader(hkxPath);
        }
        catch (Exception ex)
        {
            chain.Problems.Add($"could not read {Path.GetFileName(hkxPath)}: {ex.Message.Split('\n')[0]}");
            return null;
        }
    }
}
