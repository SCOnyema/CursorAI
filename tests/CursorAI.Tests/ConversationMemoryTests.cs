using CursorAI.App.Vision;

namespace CursorAI.Tests;

public class ConversationMemoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Recent_ReturnsTurnsOldestFirst()
    {
        ConversationMemory memory = new();
        memory.Record("first", "one", Start);
        memory.Record("second", "two", Start.AddMinutes(1));

        IReadOnlyList<ConversationTurn> turns = memory.Recent(Start.AddMinutes(2));

        Assert.Equal(["first", "second"], turns.Select(t => t.Question));
    }

    [Fact]
    public void Recent_KeepsOnlyTheNewestTurns()
    {
        ConversationMemory memory = new();
        for (int i = 0; i < ConversationMemory.MaxTurns + 3; i++)
        {
            memory.Record($"q{i}", $"a{i}", Start.AddSeconds(i));
        }

        IReadOnlyList<ConversationTurn> turns = memory.Recent(Start.AddMinutes(1));

        Assert.Equal(ConversationMemory.MaxTurns, turns.Count);
        Assert.Equal("q3", turns[0].Question);
    }

    [Fact]
    public void Recent_AfterTheIdleTimeout_StartsAFreshConversation()
    {
        // A question after lunch must not be read as a follow-up to one asked before it.
        ConversationMemory memory = new();
        memory.Record("before lunch", "answer", Start);

        Assert.Empty(memory.Recent(Start + ConversationMemory.IdleTimeout + TimeSpan.FromSeconds(1)));
        Assert.Empty(memory.Recent(Start));
    }

    [Fact]
    public void Recent_WithinTheIdleTimeout_KeepsTheConversation()
    {
        ConversationMemory memory = new();
        memory.Record("question", "answer", Start);

        Assert.Single(memory.Recent(Start + ConversationMemory.IdleTimeout));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        ConversationMemory memory = new();
        memory.Record("question", "answer", Start);

        memory.Clear();

        Assert.Empty(memory.Recent(Start));
    }

    [Fact]
    public void Record_ShortensVeryLongAnswers()
    {
        ConversationMemory memory = new();
        memory.Record("question", new string('x', ConversationMemory.MaxStoredAnswerLength * 2), Start);

        string stored = memory.Recent(Start)[0].Answer;

        Assert.Equal(ConversationMemory.MaxStoredAnswerLength + 1, stored.Length);
        Assert.EndsWith("…", stored);
    }
}
