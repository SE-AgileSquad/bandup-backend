namespace BandUp.Domain.Writing;

/// <summary>The catalog content accepted with the essay, retained independently of later archival.</summary>
public sealed record WritingPromptSnapshot(Guid Id, string Title, string PromptText);
