using System.Globalization;
using System.Text;

namespace BandUp.Application.Features.Writing;

public static class WritingEssayWordCount
{
    public static bool IsValid(string? essay) => Count(essay) is >= 100 and <= 350;

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
