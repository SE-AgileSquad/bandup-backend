using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.ReorderLessonExamples;

public sealed record ReorderLessonExamplesCommand(Guid LessonId, LessonExampleOrderInput Input) : ICommand<bool>;

public sealed record LessonExampleOrderInput(Guid[] ExampleIds);
