namespace BandUp.Domain.Writing;

public enum WritingPromptStatus
{
    Draft,
    /// <summary>Content approved by an admin and permitted for learner use.</summary>
    Published,
    Archived,
}

