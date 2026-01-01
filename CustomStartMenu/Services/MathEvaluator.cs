using System.Data;
using System.Text.RegularExpressions;

namespace CustomStartMenu.Services;

/// <summary>
/// Evaluates mathematical expressions from search queries.
/// Supports basic operations: +, -, *, /, parentheses, and decimal numbers.
/// </summary>
public static class MathEvaluator
{
    // Pattern to detect if a string looks like a math expression
    // Must contain at least one operator and consist of valid math characters
    private static readonly Regex MathExpressionPattern = new(
        @"^[\d\s\+\-\*\/\(\)\.\,]+$",
        RegexOptions.Compiled);

    // Pattern to check if expression contains at least one operator
    private static readonly Regex HasOperatorPattern = new(
        @"[\+\-\*\/]",
        RegexOptions.Compiled);

    /// <summary>
    /// Attempts to evaluate a mathematical expression.
    /// </summary>
    /// <param name="expression">The expression to evaluate (e.g., "2+2", "10*5/2", "(3+4)*2")</param>
    /// <param name="result">The calculated result if successful</param>
    /// <returns>True if the expression was valid and evaluated successfully, false otherwise</returns>
    public static bool TryEvaluate(string expression, out double result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(expression))
            return false;

        // Normalize the expression
        var normalized = NormalizeExpression(expression);

        // Check if it looks like a math expression
        if (!IsMathExpression(normalized))
            return false;

        try
        {
            // Use DataTable.Compute for evaluation
            using var table = new DataTable();
            var computed = table.Compute(normalized, null);

            if (computed == null || computed == DBNull.Value)
                return false;

            result = Convert.ToDouble(computed);

            // Check for infinity or NaN (e.g., division by zero)
            if (double.IsInfinity(result) || double.IsNaN(result))
                return false;

            return true;
        }
        catch (Exception)
        {
            // Expression was invalid
            return false;
        }
    }

    /// <summary>
    /// Formats a calculation result for display.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <returns>Formatted string representation</returns>
    public static string FormatResult(double value)
    {
        // If it's a whole number, display without decimals
        if (Math.Abs(value % 1) < 0.0000001)
        {
            return value.ToString("N0");
        }

        // Otherwise, display with up to 10 decimal places, trimming trailing zeros
        return value.ToString("G10");
    }

    /// <summary>
    /// Normalizes the expression for evaluation.
    /// </summary>
    private static string NormalizeExpression(string expression)
    {
        // Remove whitespace
        var normalized = expression.Replace(" ", "");

        // Replace comma with period for decimal separator (Turkish locale support)
        normalized = normalized.Replace(",", ".");

        return normalized;
    }

    /// <summary>
    /// Checks if the string appears to be a mathematical expression.
    /// </summary>
    private static bool IsMathExpression(string expression)
    {
        // Must match the math expression pattern
        if (!MathExpressionPattern.IsMatch(expression))
            return false;

        // Must contain at least one operator
        if (!HasOperatorPattern.IsMatch(expression))
            return false;

        // Must not be just operators or parentheses
        if (!expression.Any(char.IsDigit))
            return false;

        // Check for balanced parentheses
        var depth = 0;
        foreach (var c in expression)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;

            if (depth < 0) return false;
        }

        return depth == 0;
    }
}
