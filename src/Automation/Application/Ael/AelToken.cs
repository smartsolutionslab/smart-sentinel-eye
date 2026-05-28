namespace SmartSentinelEye.Automation.Application.Ael;

/// <summary>
/// Lexer output. <see cref="Position"/> is the 0-indexed character
/// offset of the token's first character in the source string;
/// surfaced in <see cref="AelParseException"/> for error messages.
/// </summary>
public readonly record struct AelToken(AelTokenKind Kind, string Lexeme, int Position);

public enum AelTokenKind
{
    // Literals
    IntLiteral,
    DecimalLiteral,
    StringLiteral,
    TrueLiteral,
    FalseLiteral,

    // Identifier (used for field-path segments + keyword 'contains')
    Identifier,

    // Structural
    Dollar,
    Dot,
    LeftParen,
    RightParen,

    // Arithmetic
    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    // Comparison
    Equal,             // ==
    NotEqual,          // !=
    LessThan,          // <
    LessThanOrEqual,   // <=
    GreaterThan,       // >
    GreaterThanOrEqual,// >=
    Contains,          // keyword

    // Logical
    AndAnd,            // &&
    OrOr,              // ||
    Bang,              // !

    EndOfFile,
}

public sealed class AelParseException : Exception
{
    public int Position { get; }

    public AelParseException(string message, int position)
        : base($"AEL parse error at position {position}: {message}")
    {
        Position = position;
    }
}
