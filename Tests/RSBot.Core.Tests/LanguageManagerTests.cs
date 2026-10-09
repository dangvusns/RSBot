using System.IO;
using RSBot.Core.Components;
using Xunit;

namespace RSBot.Core.Tests;

public class LanguageManagerTests
{
    [Fact]
    public void ParseLanguageFile_JoinsQuotedValueSpanningLines()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(file, new[]
            {
                "A.label2=\"If the client exits, the bot will switch",
                " to clientless mode\"",
                "A.label3=\"Single\"",
            });

            var values = LanguageManager.ParseLanguageFile(file);

            Assert.Equal("If the client exits, the bot will switch\r\n to clientless mode", values["A.label2"]);
            Assert.Equal("Single", values["A.label3"]);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
