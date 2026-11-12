using BandUp.Domain.Identity;
using BandUp.Domain.Vocabulary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Configurations.Vocabulary;

internal sealed class ReviewItemConfiguration : IEntityTypeConfiguration<ReviewItem>
{
    public void Configure(EntityTypeBuilder<ReviewItem> builder)
    {
        builder.ToTable("review_items", ParaphrasePairConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("review_items_target_check", $"""
                (item_type = '{nameof(ReviewItemType.Vocab)}'
                    AND vocab_card_id IS NOT NULL AND paraphrase_pair_id IS NULL)
                OR
                (item_type = '{nameof(ReviewItemType.Paraphrase)}'
                    AND paraphrase_pair_id IS NOT NULL AND vocab_card_id IS NULL)
                """);
            table.HasCheckConstraint("review_items_leitner_box_check", "leitner_box BETWEEN 1 AND 5");
            table.HasCheckConstraint("review_items_streak_correct_check", "streak_correct >= 0");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ItemType).HasConversion<string>();
        builder.Property(item => item.DueDate).HasColumnType("date");

        builder.HasIndex(item => new { item.UserId, item.DueDate });
        builder.HasIndex(item => new { item.UserId, item.VocabCardId })
            .IsUnique()
            .HasFilter("vocab_card_id IS NOT NULL");
        builder.HasIndex(item => new { item.UserId, item.ParaphrasePairId })
            .IsUnique()
            .HasFilter("paraphrase_pair_id IS NOT NULL");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Matching both columns prevents a review from pointing to another user's card.
        builder.HasOne<VocabCard>()
            .WithMany()
            .HasForeignKey(item => new { item.UserId, item.VocabCardId })
            .HasPrincipalKey(card => new { card.UserId, card.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ParaphrasePair>()
            .WithMany()
            .HasForeignKey(item => item.ParaphrasePairId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
