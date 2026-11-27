using System.Text.Json;
using BandUp.Application.Features.Writing;
using BandUp.Domain.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Writing;

internal sealed class WritingFeedbackConfiguration : IEntityTypeConfiguration<WritingFeedback>
{
    public void Configure(EntityTypeBuilder<WritingFeedback> builder)
    {
        builder.ToTable("writing_feedbacks", "writing", table =>
        {
            table.HasCheckConstraint("writing_feedback_band", "estimated_overall_band BETWEEN 0 AND 9 AND mod(estimated_overall_band, 0.5) = 0");
            table.HasCheckConstraint("writing_feedback_metadata", """
                (schema_version = payload->>'schemaVersion' AND payload->>'scoreLabel' = 'estimated'
                 AND estimated_overall_band = (payload->>'estimatedOverallBand')::numeric) IS TRUE
                """);
        });
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("submission_id").ValueGeneratedNever();
        builder.HasOne<WritingSubmission>().WithOne().HasForeignKey<WritingFeedback>(f => f.Id);
        builder.Ignore(f => f.Tr);
        builder.Ignore(f => f.Cc);
        builder.Ignore(f => f.Lr);
        builder.Ignore(f => f.Gra);
        builder.Property(f => f.Content).HasColumnName("payload").HasColumnType("jsonb")
            .HasConversion(content => Serialize(content), json => Deserialize(json));
    }

    // Use the same DTO as the public result so JSON naming, evidence omission and rounding stay in sync.
    private static string Serialize(WritingFeedbackContent content) =>
        JsonSerializer.Serialize(ToPublicFeedback(content), JsonSerializerOptions.Web);

    internal static WritingFeedbackDto ToPublicFeedback(WritingFeedbackContent content)
    {
        static WritingCriterionFeedbackDto Map(CriterionFeedback c) => new(c.Score, c.Explanation, c.Strengths,
            c.AreasForImprovement, c.Recommendations, c.Evidence?.Select(e => new WritingEvidenceDto(e.Excerpt, e.Observation)).ToArray());
        var criteria = new WritingCriteriaDto(Map(content.Tr), Map(content.Cc), Map(content.Lr), Map(content.Gra));
        return new WritingFeedbackDto(content.Summary, criteria);
    }

    private static WritingFeedbackContent Deserialize(string json)
    {
        var feedback = JsonSerializer.Deserialize<WritingFeedbackDto>(json, JsonSerializerOptions.Web)
            ?? throw new JsonException("Missing feedback document.");
        static CriterionFeedback Map(WritingCriterionFeedbackDto c) => new(c.Score, c.Explanation, c.Strengths,
            c.AreasForImprovement, c.Recommendations, c.Evidence?.Select(e => new WritingEvidence(e.Excerpt, e.Observation)).ToArray());
        var criteria = feedback.Criteria;
        return new WritingFeedbackContent(feedback.Summary, Map(criteria.Tr), Map(criteria.Cc), Map(criteria.Lr), Map(criteria.Gra));
    }
}
