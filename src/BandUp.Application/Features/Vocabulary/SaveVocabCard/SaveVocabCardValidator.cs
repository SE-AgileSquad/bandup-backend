using FluentValidation;

namespace BandUp.Application.Features.Vocabulary.SaveVocabCard;

public sealed class SaveVocabCardValidator : AbstractValidator<SaveVocabCardCommand>
{
    public SaveVocabCardValidator()
    {
        RuleFor(command => command.Word).NotEmpty().OverridePropertyName("word");
        RuleFor(command => command.ContextSentence).NotEmpty().OverridePropertyName("contextSentence");
        RuleFor(command => command.SourceQuestionId).NotEqual(Guid.Empty).OverridePropertyName("sourceQuestionId");
    }
}
