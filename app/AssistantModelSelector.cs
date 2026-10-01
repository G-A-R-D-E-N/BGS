namespace BehaviourStudio.App;

public sealed record AssistantProviderItem(string Wire, string Label)
{
    public override string ToString() => Label;
}
