using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OpenCommonwealth.Services.Hkx;

public sealed class AssistantInspection
{
    public BehaviorInspection InspectBehavior(
        string path, int findingLimit = AssistantInspectionLimits.DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return Failure<BehaviorInspection>("invalid_argument", "path is required");
        if (!TryLimit(findingLimit, out int limit, out var warning))
            return Failure<BehaviorInspection>("invalid_argument", "findingLimit must be greater than zero");

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadModel(path, out var model, out var report, out var code, out var message))
            return Failure<BehaviorInspection>(code, message, path, warning);

        cancellationToken.ThrowIfCancellationRequested();
        var findings = GraphValidator.Check(model!);
        var result = new BehaviorInspection
        {
            Path = path,
            File = Path.GetFileName(path),
            Readable = true,
            ObjectCount = model!.Objects.Count,
            RootObjectId = model.Objects.FirstOrDefault()?.Id ?? "",
            RootClass = model.Objects.FirstOrDefault()?.Class ?? "",
            ReferenceCount = HkReferences.In(model).Count(),
            Errors = findings.Count(f => f.Level == GraphValidator.Level.Error),
            GraphWarnings = findings.Count(f => f.Level == GraphValidator.Level.Warning),
            RoundTripLosses = report!.Count,
        };
        AddWarning(result, warning);
        result.ClassCounts.AddRange(model.Objects.GroupBy(o => o.Class, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new AssistantClassCount(g.Key, g.Count())));
        result.Findings.AddRange(findings.Take(limit).Select(Finding));
        result.RoundTrip.AddRange(report.Losses.Take(limit).Select(Loss));
        result.Truncated = findings.Count > limit || report.Count > limit;
        return result;
    }

    public ProjectChainInspection ResolveProjectChain(
        string path, int animationLimit = AssistantInspectionLimits.DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return Failure<ProjectChainInspection>("invalid_argument", "path is required");
        if (!TryLimit(animationLimit, out int limit, out var warning))
            return Failure<ProjectChainInspection>("invalid_argument", "animationLimit must be greater than zero");

        cancellationToken.ThrowIfCancellationRequested();
        if (!ExistingFile(path, out var code, out var message))
            return Failure<ProjectChainInspection>(code, message, path, warning);

        try
        {
            var chain = ProjectChain.Resolve(path);
            var result = new ProjectChainInspection
            {
                Path = path,
                File = Path.GetFileName(path),
                Root = chain.Root,
                BoneCount = chain.Bones.Count,
                SkeletonPath = chain.SkeletonPath,
            };
            AddWarning(result, warning);
            result.Links.AddRange(chain.Links.Select(l => new AssistantLink(
                l.Role, l.Declared, l.Resolved, l.Exists, Display(l.Note))));
            result.Animations.AddRange(chain.Animations.Take(limit).Select(Display));
            result.AnimationSources.AddRange(chain.AnimationSources.Take(limit)
                .Select(pair => new AssistantAnimationSource(Display(pair.Key), Display(pair.Value))));
            result.Problems.AddRange(chain.Problems.Take(AssistantInspectionLimits.HardListLimit).Select(Display));
            result.Truncated = chain.Animations.Count > limit || chain.AnimationSources.Count > limit ||
                               chain.Problems.Count > AssistantInspectionLimits.HardListLimit;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<ProjectChainInspection>("unreadable_file", "project chain could not be read", path, warning);
        }
        catch (IOException)
        {
            return Failure<ProjectChainInspection>("unreadable_file", "project chain could not be read", path, warning);
        }
        catch (Exception)
        {
            return Failure<ProjectChainInspection>("internal_failure", "project chain inspection failed", path, warning);
        }
    }

    public ProjectInspection CheckProject(
        string path, int fileLimit = AssistantInspectionLimits.DefaultListLimit,
        int findingLimitPerFile = AssistantInspectionLimits.DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return Failure<ProjectInspection>("invalid_argument", "path is required");
        if (!TryLimit(fileLimit, out int files, out var fileWarning))
            return Failure<ProjectInspection>("invalid_argument", "fileLimit must be greater than zero");
        if (!TryLimit(findingLimitPerFile, out int findings, out var findingWarning))
            return Failure<ProjectInspection>("invalid_argument", "findingLimitPerFile must be greater than zero");

        cancellationToken.ThrowIfCancellationRequested();
        if (!ExistingFile(path, out var code, out var message))
            return Failure<ProjectInspection>(code, message, path, fileWarning, findingWarning);

        try
        {
            var chain = ProjectChain.Resolve(path);
            var checkedProject = ProjectCheck.Run(chain);
            var result = new ProjectInspection
            {
                Path = path,
                File = Path.GetFileName(path),
                Root = chain.Root,
                FilesFound = checkedProject.Files.Count,
                FilesReturned = Math.Min(files, checkedProject.Files.Count),
                Errors = checkedProject.Errors,
                GraphWarnings = checkedProject.Warnings,
                Unreadable = checkedProject.Unreadable,
                RoundTripLosses = checkedProject.RoundTripLosses,
                FilesWithRoundTripLosses = checkedProject.FilesWithRoundTripLosses,
            };
            AddWarning(result, fileWarning);
            AddWarning(result, findingWarning);
            foreach (var file in checkedProject.Files.Take(files))
            {
                var item = new ProjectFileInspection
                {
                    Path = file.Path,
                    Name = file.Name,
                    Error = Display(file.Error),
                    Errors = file.Errors,
                    GraphErrors = file.GraphErrors,
                    GraphWarnings = file.GraphWarnings,
                    RoundTripLosses = file.RoundTripLosses,
                };
                item.Findings.AddRange(file.Findings.Take(findings).Select(Finding));
                item.RoundTrip.AddRange(file.RoundTrip.Losses.Take(findings).Select(Loss));
                item.Truncated = file.Findings.Count > findings || file.RoundTrip.Count > findings;
                result.Files.Add(item);
            }
            result.Truncated = checkedProject.Files.Count > files ||
                               checkedProject.Files.Take(files).Any(file =>
                                   file.Findings.Count > findings || file.RoundTrip.Count > findings);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<ProjectInspection>("unreadable_file", "project could not be read", path, fileWarning, findingWarning);
        }
        catch (IOException)
        {
            return Failure<ProjectInspection>("unreadable_file", "project could not be read", path, fileWarning, findingWarning);
        }
        catch (Exception)
        {
            return Failure<ProjectInspection>("internal_failure", "project inspection failed", path, fileWarning, findingWarning);
        }
    }

    public SearchInspection SearchProject(
        string path, string query, int limit = AssistantInspectionLimits.DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return Failure<SearchInspection>("invalid_argument", "path is required");
        if (string.IsNullOrWhiteSpace(query)) return Failure<SearchInspection>("invalid_argument", "query is required", path);
        if (!TryLimit(limit, out int effective, out var warning))
            return Failure<SearchInspection>("invalid_argument", "limit must be greater than zero", path);

        cancellationToken.ThrowIfCancellationRequested();
        if (!ExistingFile(path, out var code, out var message))
            return Failure<SearchInspection>(code, message, path, warning);

        try
        {
            var chain = ProjectChain.Resolve(path);
            var searched = ProjectSearch.Run(chain, query.Trim(), effective);
            var result = new SearchInspection
            {
                Path = path,
                File = Path.GetFileName(path),
                Query = query.Trim(),
                FilesFound = searched.FilesFound,
                FilesRead = searched.FilesRead,
                FilesUnreadable = searched.FilesUnreadable,
            };
            AddWarning(result, warning);
            result.Hits.AddRange(searched.Hits.Select(hit => new AssistantSearchHit(
                hit.Path, hit.File, hit.Kind, hit.ObjectId, hit.ClassName, hit.Field, Display(hit.Value))));
            result.Problems.AddRange(searched.Problems.Select(problem => new AssistantSearchProblem(
                problem.Path, problem.File, Display(problem.Error))));
            result.Truncated = searched.Truncated;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<SearchInspection>("unreadable_file", "project search could not be read", path, warning);
        }
        catch (IOException)
        {
            return Failure<SearchInspection>("unreadable_file", "project search could not be read", path, warning);
        }
        catch (Exception)
        {
            return Failure<SearchInspection>("internal_failure", "project search failed", path, warning);
        }
    }

    public AnimationInspection InspectAnimation(
        string path, int annotationLimit = AssistantInspectionLimits.DefaultListLimit,
        int boneLimit = AssistantInspectionLimits.DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return Failure<AnimationInspection>("invalid_argument", "path is required");
        if (!TryLimit(annotationLimit, out int annotations, out var annotationWarning))
            return Failure<AnimationInspection>("invalid_argument", "annotationLimit must be greater than zero", path);
        if (!TryLimit(boneLimit, out int bones, out var boneWarning))
            return Failure<AnimationInspection>("invalid_argument", "boneLimit must be greater than zero", path, annotationWarning);

        cancellationToken.ThrowIfCancellationRequested();
        if (!ExistingFile(path, out var code, out var message))
            return Failure<AnimationInspection>(code, message, path, annotationWarning, boneWarning);

        try
        {
            var report = RoundTripReport.ForFile(path);
            bool supported = new HkxBinaryReader().TryReadAnimation(path, out var animation);
            var result = new AnimationInspection
            {
                Path = path,
                File = Path.GetFileName(path),
                AnimationClass = animation.AnimationClass,
                Supported = supported,
                Duration = animation.Duration,
                NumFrames = animation.NumFrames,
                Fps = animation.FrameDuration > 0 ? 1f / animation.FrameDuration : 0,
                NumTracks = animation.NumTracks,
                NumBlocks = animation.NumBlocks,
                BoneCount = animation.BoneNames.Count,
                AnnotationCount = animation.Annotations.Count,
                OriginalSkeletonName = Display(animation.OriginalSkeletonName),
                BlendHint = animation.BlendHint,
            };
            AddWarning(result, annotationWarning);
            AddWarning(result, boneWarning);
            result.Bones.AddRange(animation.BoneNames.Take(bones).Select(Display));
            result.Annotations.AddRange(animation.Annotations.Take(annotations)
                .Select(note => new AssistantAnnotation(note.Time, Display(note.Text))));
            result.RoundTrip.AddRange(report.Losses.Take(AssistantInspectionLimits.HardListLimit).Select(Loss));
            result.Truncated = animation.BoneNames.Count > bones || animation.Annotations.Count > annotations ||
                               report.Count > AssistantInspectionLimits.HardListLimit;
            if (!supported)
            {
                result = WithStatus(result, "partial", "unsupported_hkx",
                    animation.AnimationClass.Length == 0 ? "file has no supported animation" :
                    $"animation class {Display(animation.AnimationClass)} is not decoded");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<AnimationInspection>("unreadable_file", "animation could not be read", path, annotationWarning, boneWarning);
        }
        catch (InvalidDataException)
        {
            return Failure<AnimationInspection>("unreadable_file", "animation could not be read", path, annotationWarning, boneWarning);
        }
        catch (NotSupportedException)
        {
            return Failure<AnimationInspection>("unsupported_hkx", "animation format is not supported", path, annotationWarning, boneWarning);
        }
        catch (Exception)
        {
            return Failure<AnimationInspection>("internal_failure", "animation inspection failed", path, annotationWarning, boneWarning);
        }
    }

    public ObjectInspection InspectObject(
        string path, string objectId, int fieldLimit = AssistantInspectionLimits.DefaultListLimit,
        int referenceLimit = AssistantInspectionLimits.DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(objectId))
            return Failure<ObjectInspection>("invalid_argument", "path and objectId are required", path);
        if (!TryLimit(fieldLimit, out int fields, out var fieldWarning))
            return Failure<ObjectInspection>("invalid_argument", "fieldLimit must be greater than zero", path);
        if (!TryLimit(referenceLimit, out int references, out var referenceWarning))
            return Failure<ObjectInspection>("invalid_argument", "referenceLimit must be greater than zero", path, fieldWarning);

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadModel(path, out var model, out _, out var code, out var message))
            return Failure<ObjectInspection>(code, message, path, fieldWarning, referenceWarning);

        var id = objectId.TrimStart('#');
        var obj = model!.Get(id);
        if (obj == null)
            return Failure<ObjectInspection>("object_not_found", $"object #{id} was not found", path, fieldWarning, referenceWarning);

        var result = new ObjectInspection
        {
            Path = path,
            File = Path.GetFileName(path),
            ObjectId = id,
            ClassName = obj.Class,
        };
        AddWarning(result, fieldWarning);
        AddWarning(result, referenceWarning);
        AddFields(result.Scalars, obj.Scalars.OrderBy(pair => pair.Key), fields, "scalar");
        AddFields(result.Lists, obj.Lists.OrderBy(pair => pair.Key)
            .Select(pair => new KeyValuePair<string, IReadOnlyList<string>>(pair.Key, pair.Value)), fields, "list");
        AddFields(result.Structs, obj.Structs.OrderBy(pair => pair.Key)
            .Select(pair => new KeyValuePair<string, IReadOnlyList<string>>(pair.Key,
                pair.Value.Select(member => member.Key + "=" + Display(member.Value)).ToList())), fields, "struct");
        AddFields(result.StructLists, obj.StructLists.OrderBy(pair => pair.Key)
            .Select(pair => new KeyValuePair<string, IReadOnlyList<string>>(pair.Key,
                pair.Value.Select(row => string.Join(", ", row.OrderBy(member => member.Key)
                    .Select(member => member.Key + "=" + Display(member.Value)))).ToList())), fields, "structList");

        var outgoing = HkReferences.In(obj).ToList();
        var incoming = HkReferences.In(model).Where(site => site.Target == id).ToList();
        result.OutgoingReferences.AddRange(outgoing.Take(references).Select(Reference));
        result.IncomingReferences.AddRange(incoming.Take(references).Select(Reference));
        result.ElementSummaries.AddRange(ElementSummary.For(model, id).OrderBy(pair => pair.Key)
            .Take(fields).Select(pair => new AssistantElementSummary(pair.Key, Display(pair.Value))));
        result.Truncated = obj.Scalars.Count + obj.Lists.Count + obj.Structs.Count + obj.StructLists.Count > fields ||
                           outgoing.Count > references || incoming.Count > references ||
                           ElementSummary.For(model, id).Count > fields;
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static void AddFields(List<AssistantObjectField> target,
                                   IEnumerable<KeyValuePair<string, string>> fields,
                                   int limit, string kind)
    {
        foreach (var field in fields.Take(limit))
            target.Add(new AssistantObjectField(field.Key, kind, 1, Display(field.Value), new[] { Display(field.Value) }));
    }

    private static void AddFields(List<AssistantObjectField> target,
                                  IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> fields,
                                  int limit, string kind)
    {
        foreach (var field in fields.Take(limit))
        {
            var sample = field.Value.Take(limit).Select(Display).ToList();
            target.Add(new AssistantObjectField(field.Key, kind, field.Value.Count,
                field.Value.Count == 1 ? sample[0] : "", sample));
        }
    }

    private static AssistantReference Reference(HkReferences.Site site) => new(
        site.HolderId, site.Target, site.Field, site.Index, site.Member, site.How.ToString(), site.Path());

    private static AssistantFinding Finding(GraphValidator.Finding finding) => new(
        finding.Level.ToString().ToLowerInvariant(), Display(finding.Where), Display(finding.What),
        finding.ObjectId, finding.BlocksSave);

    private static AssistantRoundTripLoss Loss(RoundTripLoss loss) => new(
        loss.Kind.ToString(), loss.ObjectId, Display(loss.Member), Display(loss.Message));

    private static bool TryReadModel(string path, out BehaviourGraphModel? model,
                                     out RoundTripReport? report, out string code, out string message)
    {
        model = null;
        report = null;
        code = "ok";
        message = "";
        if (!ExistingFile(path, out code, out message)) return false;

        try
        {
            report = RoundTripReport.ForFile(path);
            string xml = HkxTextEdit.TextOf(path);
            if (xml.Length == 0)
            {
                code = report.Losses.Any(loss => loss.Kind is RoundTripLossKind.UnsupportedClass or
                                                 RoundTripLossKind.UnsupportedAnimation)
                    ? "unsupported_hkx" : "unreadable_file";
                message = code == "unsupported_hkx" ? "file contains unsupported HKX content" :
                    "file could not be read as a behavior graph";
                return false;
            }

            model = BehaviourGraphModel.Parse(xml);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            code = "unreadable_file";
            message = "file could not be read";
            return false;
        }
        catch (InvalidDataException)
        {
            code = "unreadable_file";
            message = "file could not be read";
            return false;
        }
        catch (NotSupportedException)
        {
            code = "unsupported_hkx";
            message = "file format is not supported";
            return false;
        }
        catch (Exception)
        {
            code = "internal_failure";
            message = "behavior inspection failed";
            return false;
        }
    }

    private static bool ExistingFile(string path, out string code, out string message)
    {
        code = "ok";
        message = "";
        if (!File.Exists(path))
        {
            code = Directory.Exists(path) ? "path_not_file" : "path_not_found";
            message = code == "path_not_file" ? "path is not a file" : "file was not found";
            return false;
        }
        return true;
    }

    private static bool TryLimit(int requested, out int effective, out string? warning)
    {
        effective = requested;
        warning = null;
        if (requested <= 0) return false;
        if (requested <= AssistantInspectionLimits.HardListLimit) return true;
        effective = AssistantInspectionLimits.HardListLimit;
        warning = $"requested limit was clamped to {AssistantInspectionLimits.HardListLimit}";
        return true;
    }

    private static T Failure<T>(string code, string message, string path = "", params string?[] warnings)
        where T : AssistantInspectionResult, new()
    {
        var result = new T
        {
            Source = "disk",
            Status = "error",
            Code = code,
            Message = message,
        };
        foreach (string? warning in warnings) AddWarning(result, warning);
        return result;
    }

    private static void AddWarning(AssistantInspectionResult result, string? warning)
    {
        if (!string.IsNullOrEmpty(warning)) result.Warnings.Add(warning);
    }

    private static T WithStatus<T>(T result, string status, string code, string message)
        where T : AssistantInspectionResult
    {
        result.Status = status;
        result.Code = code;
        result.Message = message;
        return result;
    }

    private static string Display(string value)
    {
        string oneLine = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= AssistantInspectionLimits.MaxDisplayString
            ? oneLine : oneLine[..AssistantInspectionLimits.MaxDisplayString] + "…";
    }
}
