namespace CursorAI.App.Vision;

/// <summary>
/// One completed exchange, kept so a later question can refer back to it.
/// </summary>
public sealed record ConversationTurn(string Question, string Answer, DateTimeOffset At);

/// <summary>
/// The recent questions and answers of this session, so "and then what?" means something.
/// </summary>
/// <remarks>
/// Held in memory only. Nothing is written to disk and quitting forgets it, because answers
/// are derived from screenshots and can repeat whatever was on screen.
///
/// Text only: earlier screenshots are not kept or re-sent. The current screenshot is always
/// attached, so the model sees what is on screen now, and resending old images would multiply
/// request size and latency for little gain.
///
/// A conversation ends after <see cref="IdleTimeout"/> without a question, so a question asked
/// after lunch is not read as a follow-up to one asked before it.
/// </remarks>
public sealed class ConversationMemory
{
    /// <summary>Turns kept. Enough for a short back-and-forth without growing every request.</summary>
    internal const int MaxTurns = 6;

    /// <summary>
    /// Longest stored answer. An answer with numbered steps can run long, and only its gist is
    /// needed to resolve "it" or "the next step" in a follow-up.
    /// </summary>
    internal const int MaxStoredAnswerLength = 1500;

    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly List<ConversationTurn> _turns = [];

    /// <summary>
    /// Turns still in the conversation, oldest first. Empty once the conversation has been idle
    /// past <see cref="IdleTimeout"/>, which also ends it.
    /// </summary>
    public IReadOnlyList<ConversationTurn> Recent(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_turns.Count > 0 && now - _turns[^1].At > IdleTimeout)
            {
                _turns.Clear();
            }

            return [.. _turns];
        }
    }

    /// <summary>Records a completed exchange. Only successful answers belong here.</summary>
    public void Record(string question, string answer, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);

        string stored = answer.Trim();
        if (stored.Length > MaxStoredAnswerLength)
        {
            stored = stored[..MaxStoredAnswerLength] + "…";
        }

        lock (_gate)
        {
            _turns.Add(new ConversationTurn(question.Trim(), stored, at));
            if (_turns.Count > MaxTurns)
            {
                _turns.RemoveRange(0, _turns.Count - MaxTurns);
            }
        }
    }

    /// <summary>Starts a new conversation.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _turns.Clear();
        }
    }
}
