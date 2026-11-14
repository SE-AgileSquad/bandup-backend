using BandUp.Domain.Common;

namespace BandUp.Domain.Content;

public sealed class AnswerKey : Entity
{
    private const string NotGiven = "Not Given";

    private readonly List<string> _acceptedVariants = [];

    private AnswerKey()
    {
    }

    internal AnswerKey(
        Guid questionId,
        QuestionType type,
        string correctAnswer,
        IEnumerable<string>? acceptedVariants)
    {
        QuestionId = questionId;
        SetAnswer(type, correctAnswer, acceptedVariants);
    }

    public Guid QuestionId { get; private set; }
    public string CorrectAnswer { get; private set; } = null!;
    public IReadOnlyList<string> AcceptedVariants => _acceptedVariants.AsReadOnly();
    public Guid? VerifiedBy { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public bool IsVerified => VerifiedBy.HasValue && VerifiedAt.HasValue;

    internal bool IsNotGiven(QuestionType type) =>
        type == QuestionType.TrueFalseNotGiven && CorrectAnswer == NotGiven;

    internal void SetAnswer(QuestionType type, string correctAnswer, IEnumerable<string>? acceptedVariants)
    {
        if (string.IsNullOrWhiteSpace(correctAnswer))
        {
            throw new DomainException("An answer key requires a correct answer.");
        }

        string answer = correctAnswer.Trim();
        string[] variants = acceptedVariants?.ToArray() ?? [];

        if (type == QuestionType.TrueFalseNotGiven)
        {
            if (variants.Length != 0)
            {
                throw new DomainException("Accepted variants are only allowed for sentence completion.");
            }

            answer = answer.ToUpperInvariant() switch
            {
                "TRUE" => "True",
                "FALSE" => "False",
                "NOT GIVEN" => NotGiven,
                _ => throw new DomainException("A TFNG answer must be True, False or Not Given."),
            };
        }

        if (variants.Any(string.IsNullOrWhiteSpace))
        {
            throw new DomainException("Accepted variants cannot be blank.");
        }

        CorrectAnswer = answer;
        _acceptedVariants.Clear();
        _acceptedVariants.AddRange(variants.Select(variant => variant.Trim()));
        InvalidateVerification();
    }

    internal void Verify(Guid verifiedBy, DateTimeOffset verifiedAt)
    {
        if (verifiedBy == Guid.Empty)
        {
            throw new DomainException("An answer key requires a verifier.");
        }

        VerifiedBy = verifiedBy;
        VerifiedAt = verifiedAt;
    }

    internal void InvalidateVerification()
    {
        VerifiedBy = null;
        VerifiedAt = null;
    }
}
