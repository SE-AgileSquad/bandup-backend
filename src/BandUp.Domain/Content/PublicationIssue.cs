namespace BandUp.Domain.Content;

public sealed record PublicationIssue(string Field, string Message, Guid? QuestionId = null);
