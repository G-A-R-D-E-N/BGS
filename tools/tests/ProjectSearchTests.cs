using System;
using System.Collections.Generic;
using System.IO;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ProjectSearchTests
{
    [Fact]
    public void RunFindsNamesAssetsSymbolsClassesAndNestedFields()
    {
        using var scope = new SearchScope();
        string movement = scope.Add("movement.hkx", Movement());
        scope.Add("combat.hkx", Combat());

        var names = ProjectSearch.Run(scope.Chain, "sprint", modelReader: scope.Read);
        Assert.Equal(2, names.FilesRead);
        Assert.Contains(names.Hits, hit =>
            hit.Path == movement && hit.Kind == "name" && hit.Field == "name" && hit.Value == "Sprint");
        Assert.Contains(names.Hits, hit =>
            hit.Path == movement && hit.Kind == "asset" && hit.Field == "animationName"
            && hit.Value.Contains("Sprint.hkx", StringComparison.Ordinal));

        var symbols = ProjectSearch.Run(scope.Chain, "speed", modelReader: scope.Read);
        Assert.Contains(symbols.Hits, hit => hit.Kind == "variable" && hit.Value == "Speed");
        Assert.Contains(symbols.Hits, hit => hit.Field == "metadata.displayName" && hit.Value == "Player Speed");
        Assert.Contains(symbols.Hits, hit => hit.Field == "bindings[0].memberPath" && hit.Value == "speedFraction");

        var classes = ProjectSearch.Run(scope.Chain, "hkbClipGenerator", modelReader: scope.Read);
        Assert.Contains(classes.Hits, hit => hit.Kind == "class" && hit.ClassName == "hkbClipGenerator");
    }

    [Fact]
    public void RunContainsUnreadableFilesAndKeepsOtherResults()
    {
        using var scope = new SearchScope();
        scope.Add("good.hkx", Movement());
        string bad = scope.Add("bad.hkx", null);

        var result = ProjectSearch.Run(scope.Chain, "Sprint", modelReader: path =>
        {
            if (path == bad) throw new InvalidDataException("broken fixture\nsecond line");
            return scope.Read(path);
        });

        Assert.Equal(2, result.FilesFound);
        Assert.Equal(1, result.FilesRead);
        Assert.Single(result.Problems);
        Assert.Equal("broken fixture", result.Problems[0].Error);
        Assert.NotEmpty(result.Hits);
        Assert.Contains("1 unreadable", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunHonorsTheResultLimit()
    {
        using var scope = new SearchScope();
        var model = new BehaviourGraphModel();
        for (int i = 0; i < 20; i++)
        {
            var obj = Add(model, (90 + i).ToString(), "hkbClipGenerator");
            obj.Scalars["name"] = "match " + i;
        }
        scope.Add("many.hkx", model);

        var result = ProjectSearch.Run(scope.Chain, "match", resultLimit: 5, modelReader: scope.Read);

        Assert.Equal(5, result.Hits.Count);
        Assert.True(result.Truncated);
        Assert.Contains("stopped at 5", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunCanMatchAFileNameAndDeduplicatesTheSameLocation()
    {
        using var scope = new SearchScope();
        scope.Add("Dogmeat_Locomotion.hkx", Combat());

        var result = ProjectSearch.Run(scope.Chain, "dogmeat", modelReader: scope.Read);

        Assert.Single(result.Hits);
        Assert.Equal("file", result.Hits[0].Kind);
        Assert.Equal("Dogmeat_Locomotion.hkx", result.Hits[0].File);
    }

    [Fact]
    public void BlankQueryDoesNotTouchTheProjectTree()
    {
        var chain = new ProjectChain { Root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) };
        bool read = false;

        var result = ProjectSearch.Run(chain, "   ", modelReader: _ =>
        {
            read = true;
            return new BehaviourGraphModel();
        });

        Assert.False(read);
        Assert.Empty(result.Hits);
        Assert.Empty(result.Problems);
        Assert.Equal(0, result.FilesFound);
    }

    private static BehaviourGraphModel Movement()
    {
        var model = new BehaviourGraphModel();
        var clip = Add(model, "90", "hkbClipGenerator");
        clip.Scalars["name"] = "Sprint";
        clip.Scalars["animationName"] = "Animations/Movement/Sprint.hkx";
        clip.Structs["metadata"] = new Dictionary<string, string>
        {
            ["displayName"] = "Player Speed",
        };
        clip.StructLists["bindings"] = new List<Dictionary<string, string>>
        {
            new() { ["memberPath"] = "speedFraction", ["variableIndex"] = "0" },
        };

        var strings = Add(model, "91", "hkbBehaviorGraphStringData");
        strings.Lists["eventNames"] = new List<string> { "Jump", "Land" };
        strings.Lists["variableNames"] = new List<string> { "Speed", "Direction" };
        return model;
    }

    private static BehaviourGraphModel Combat()
    {
        var model = new BehaviourGraphModel();
        var state = Add(model, "90", "hkbStateMachineStateInfo");
        state.Scalars["name"] = "Attack";
        state.Scalars["stateId"] = "2";
        return model;
    }

    private static HkObject Add(BehaviourGraphModel model, string id, string className)
    {
        var obj = new HkObject { Id = id, Class = className };
        model.ById[id] = obj;
        model.Objects.Add(obj);
        return obj;
    }

    private sealed class SearchScope : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("bgs-project-search").FullName;
        private readonly Dictionary<string, BehaviourGraphModel?> _models =
            new(StringComparer.OrdinalIgnoreCase);

        public SearchScope()
        {
            string behaviors = Path.Combine(_root, "Behaviors");
            Directory.CreateDirectory(behaviors);
            Chain = new ProjectChain { Root = _root };
        }

        public ProjectChain Chain { get; }

        public string Add(string name, BehaviourGraphModel? model)
        {
            string path = Path.Combine(_root, "Behaviors", name);
            File.WriteAllBytes(path, new byte[] { 0 });
            _models[path] = model;
            return path;
        }

        public BehaviourGraphModel? Read(string path) => _models[path];

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
