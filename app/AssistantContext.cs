namespace BehaviourStudio.App;

public sealed record AssistantContext(
    string DocumentId,
    string DocumentStamp,
    string Path,
    string ActiveActivity,
    string SelectedObjectId,
    string SelectedObjectClass,
    bool IsDirty,
    bool AnimationEdited,
    int ProblemCount,
    string ProjectRoot,
    string AnimationClass,
    int SelectedTrack = -1,
    int SelectedFrame = -1);
