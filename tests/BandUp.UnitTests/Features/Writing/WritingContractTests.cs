using BandUp.Domain.Writing;
using System.Text.Json;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Infrastructure;

namespace BandUp.UnitTests.Features.Writing;

public sealed class WritingContractTests
{
    [Fact]
    public void PublishedExamplesMatchWireContracts()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "BandUp.sln")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        string examples = Path.Combine(root.FullName, "docs", "writing", "examples");
        SubmitWritingEssayRequest submission = Verify<SubmitWritingEssayRequest>("submission");
        Assert.True(WritingEssayWordCount.IsValid(submission.EssayText));
        var result = Verify<WritingEvaluationResultDto>("result");
        var prompts = Verify<WritingPromptListDto>("prompts");
        var history = Verify<PagedResult<WritingSubmissionHistoryItemDto>>("history");
        var detail = Verify<WritingSubmissionDetailDto>("detail");
        WritingSubmissionHistoryItemDto item = Assert.Single(history.Items);
        Assert.Equal(submission.PromptId, detail.PromptSnapshot.Id);
        Assert.Equal(submission.EssayText, detail.EssayText);
        Assert.Equal(Assert.Single(prompts.Items), detail.PromptSnapshot);
        Assert.Equal(detail.SubmissionId, item.SubmissionId);
        Assert.Equal(result.SubmissionId, item.SubmissionId);
        Assert.Equal(detail.PromptSnapshot.Id, item.PromptId);
        Assert.Equal(detail.PromptSnapshot.Title, item.Title);
        Assert.Equal(result.CompletedAt, item.CompletedAt);
        Assert.Equal(result.Feedback.EstimatedOverallBand, item.EstimatedOverallBand);
        var page = new PageRequest();
        Assert.Equal(page.Page, history.Page);
        Assert.Equal(page.PageSize, history.PageSize);

        T Verify<T>(string name) where T : class
        {
            string content = File.ReadAllText(Path.Combine(examples, name + ".json"));
            T? value = JsonSerializer.Deserialize<T>(content, JsonSerialization.Options);
            Assert.NotNull(value);
            JsonElement actual = JsonSerializer.SerializeToElement(value, JsonSerialization.Options);
            using JsonDocument expected = JsonDocument.Parse(content);
            Assert.True(JsonElement.DeepEquals(expected.RootElement, actual));
            return value;
        }
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(350, true)]
    [InlineData(351, false)]
    public void CountsInclusiveBoundaries(int words, bool valid)
    {
        Assert.Equal(valid, WritingEssayWordCount.IsValid(string.Join(" ", Enumerable.Repeat("word", words))));
    }

    [Theory]
    [InlineData("don't don’t well-being word--word", 5)]
    [InlineData(" \t café cafe\u0301 ３５０ — 😊", 3)]
    [InlineData("'---' 3.5 IELTS_2", 4)]
    [InlineData(" \t\r\n\u2003", 0)]
    [InlineData("x\u0301-y x-\u0301y x--y", 5)]
    [InlineData("𐐀-𐐁 क्", 2)]
    public void CountsUnicodeTokens(string text, int count)
    {
        Assert.Equal(count, WritingEssayWordCount.Count(text));
    }

    [Theory]
    [InlineData(6, 6, 6, 6.5, 6)]
    [InlineData(6, 6, 6.5, 6.5, 6.5)]
    [InlineData(6, 7, 7, 7, 7)]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(9, 9, 9, 9, 9)]
    public void ComputesEstimatedBand(double tr, double cc, double lr, double gra, double expected)
    {
        static WritingCriterionFeedbackDto Criterion(double score) => new((decimal)score, "text", [], [], ["action"]);
        WritingCriteriaDto criteria = new(Criterion(tr), Criterion(cc), Criterion(lr), Criterion(gra));
        WritingFeedbackDto feedback = new("summary", criteria);
        Assert.Equal((decimal)expected, feedback.EstimatedOverallBand);
        Assert.Equal("estimated", feedback.ScoreLabel);
        Assert.Equal("1.0", feedback.SchemaVersion);
    }

    [Theory]
    [InlineData(0, -0.5)]
    [InlineData(1, 9.5)]
    [InlineData(2, 6.25)]
    [InlineData(3, 0.1)]
    public void InvalidCriterionRejectsOverall(int index, double score)
    {
        WritingCriterionFeedbackDto valid = new(6, "text", [], [], ["action"]);
        WritingCriterionFeedbackDto[] values = [valid, valid, valid, valid];
        values[index] = valid with { Score = (decimal)score };
        WritingFeedbackDto feedback = new("summary", new(values[0], values[1], values[2], values[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => feedback.EstimatedOverallBand);
    }

    [Theory]
    [InlineData(WritingEvaluationStatus.Queued)]
    [InlineData(WritingEvaluationStatus.Processing)]
    [InlineData(WritingEvaluationStatus.Succeeded)]
    [InlineData(WritingEvaluationStatus.Failed)]
    public void SerializesStateFields(WritingEvaluationStatus status)
    {
        bool succeeded = status == WritingEvaluationStatus.Succeeded;
        bool failed = status == WritingEvaluationStatus.Failed;
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        WritingEvaluationStatusDto value = new(Guid.Empty, status, now, succeeded || failed ? now : null,
            succeeded ? "/result" : null, failed ? new(WritingErrorCodes.EvaluationFailed) : null);
        JsonElement json = JsonSerializer.SerializeToElement(value, JsonSerialization.Options);
        Assert.Equal(status.ToString(), json.GetProperty("status").GetString());
        Assert.Equal(succeeded, json.TryGetProperty("resultUrl", out _));
        Assert.Equal(failed, json.TryGetProperty("failure", out _));
        Assert.Equal(succeeded || failed, json.TryGetProperty("completedAt", out _));
        if (failed)
        {
            Assert.Equal("EVALUATION_FAILED", json.GetProperty("failure").GetProperty("code").GetString());
        }
    }
}
