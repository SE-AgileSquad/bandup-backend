namespace BandUp.Application.Features.Exams;

/// <summary>
/// One approved paraphrase pair of a question, shown in the review so the learner can see how
/// the question's wording maps onto the passage. The two sentences are returned exactly as the
/// admin stored them.
/// </summary>
public sealed record ParaphrasePairDto(string SourceSentence, string EquivalentSentence);
