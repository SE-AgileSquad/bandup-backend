using System.Globalization;
using System.Text;

namespace BandUp.Domain.Writing;

public static class EssayPolicy
{
    public const int MaxScalars = 8000;

    public static bool IsStructurallySafe(string? essay) => essay is not null && FitsScalarLimit(essay, MaxScalars);

    public static bool IsValid(string? essay) => IsStructurallySafe(essay) && Count(essay) is >= 100 and <= 350;

    internal static bool FitsScalarLimit(string text, int maximum)
    {
        ReadOnlySpan<char> remaining = text.AsSpan();
        int count = 0;
        while (!remaining.IsEmpty)
        {
            if (++count > maximum || Rune.DecodeFromUtf16(remaining, out Rune rune, out int consumed)
                != System.Buffers.OperationStatus.Done || rune.Value == 0)
            {
                return false;
            }
            remaining = remaining[consumed..];
        }
        return true;
    }

    public static int Count(string? essay)
    {
        if (string.IsNullOrEmpty(essay))
        {
            return 0;
        }

        int count = 0;
        bool inWord = false;
        bool afterConnector = false;
        foreach (Rune rune in essay.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || Rune.IsNumber(rune))
            {
                if (!inWord)
                {
                    count++;
                }
                inWord = true;
                afterConnector = false;
                continue;
            }

            if (inWord && !afterConnector)
            {
                if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                {
                    continue;
                }
                if (rune.Value is '-' or '\'' or '’')
                {
                    afterConnector = true;
                    continue;
                }
            }

            inWord = false;
            afterConnector = false;
        }
        return count;
    }
}
