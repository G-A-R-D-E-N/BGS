using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// An object's Havok setup is five files, not one: project names the character, character names the
// skeleton, the behaviour and the animation list. Every reference is relative to the project folder,
// which is why a folder can be cloned under a new name without editing anything inside it.
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

    public static ProjectChain Resolve(string anyHkxPath, string java, string jar)
    {
        var chain = new ProjectChain();
        string dir = Path.GetDirectoryName(Path.GetFullPath(anyHkxPath)) ?? "";

        // Behaviours sit in <project>/Behaviors, characters in <project>/Characters, so the project
        // root is one level up from either.
        string leaf = Path.GetFileName(dir);
        chain.Root = leaf.Equals("Behaviors", StringComparison.OrdinalIgnoreCase)
                  || leaf.Equals("Characters", StringComparison.OrdinalIgnoreCase)
                  || leaf.Equals("CharacterAssets", StringComparison.OrdinalIgnoreCase)
                  || leaf.Equals("Animations", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(dir) ?? dir
            : dir;

        string? projectFile = Directory.EnumerateFiles(chain.Root, "*.hkx", SearchOption.TopDirectoryOnly)
                                       .FirstOrDefault();
        if (projectFile == null)
        {
            chain.Problems.Add($"no project .hkx directly under {chain.Root}");
            return chain;
        }

        chain.Add("project", Path.GetFileName(projectFile), projectFile);

        var project = Read(projectFile, java, jar, chain);
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

        var character = Read(characterPath, java, jar, chain);
        var strings = character?.Objects.FirstOrDefault(o => o.Class == "hkbCharacterStringData");
        if (strings == null)
        {
            chain.Problems.Add("the character file has no hkbCharacterStringData");
            return chain;
        }

        // behaviorFilename, rigName and animationNames are relative to the PROJECT root, not to the
        // folder the character file happens to sit in.
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
            if (!File.Exists(ResolvePath(chain.Root, anim)))
                chain.Problems.Add("missing animation: " + anim);
        }

        return chain;
    }

    // Skyrim era characters list their animations in animationNames. Fallout 4 moved them into
    // animationBundleNameData, whose assetNames the model parser flattens onto the outer param, so
    // reading only the old field leaves this empty for every vanilla FO4 character.
    public static List<string> DeclaredAnimations(HkObject characterStringData)
    {
        var all = new List<string>(characterStringData.Strings("animationNames"));
        foreach (string name in characterStringData.Strings("animationBundleNameData"))
            if (!all.Contains(name)) all.Add(name);
        return all;
    }

    // A clip names its animation the same way the character declares it, but the two can differ in
    // separator and in the .hkt/.hkx split, so compare on this rather than on the raw string.
    public static string AnimationKey(string declared)
        => Path.ChangeExtension(declared.Replace('/', '\\'), null).ToLowerInvariant();

    // Fallout 4 declares these as .hkt but ships .hkx on disk, so a plain join misses every file.
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

    private static BehaviourGraphModel? Read(string hkxPath, string java, string jar, ProjectChain chain)
    {
        try
        {
            string work = Path.Combine(Path.GetTempPath(), "oc_chain", Path.GetFileNameWithoutExtension(hkxPath));
            if (Directory.Exists(work)) Directory.Delete(work, true);
            string xml = HkxTextEdit.Unpack(java, jar, hkxPath, work);
            return BehaviourGraphModel.Parse(HkxTextEdit.ReadXml(xml));
        }
        catch (Exception ex)
        {
            chain.Problems.Add($"could not read {Path.GetFileName(hkxPath)}: {ex.Message.Split('\n')[0]}");
            return null;
        }
    }
}
