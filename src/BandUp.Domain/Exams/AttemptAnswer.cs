using BandUp.Domain.Common;

namespace BandUp.Domain.Exams;

/// <summary>
/// The learner's current answer to one question of an attempt. <see cref="IsCorrect"/> stays
/// null until the attempt is submitted and graded.
/// </summary>
public sealed class AttemptAnswer : Entity
{
    private AttemptAnswer()
    {
    }

    internal AttemptAnswer(Guid attemptId, Guid questionId, string selectedAnswer)
    {
        AttemptId = attemptId;
        QuestionId = questionId;
        SelectedAnswer = selectedAnswer;
    }

    public Guid AttemptId { get; private set; }

    public Guid QuestionId { get; private set; }

    public string SelectedAnswer { get; private set; } = null!;

    public bool? IsCorrect { get; private set; }

    internal void Change(string selectedAnswer) => SelectedAnswer = selectedAnswer;

    internal void Grade(bool isCorrect) => IsCorrect = isCorrect;
}
